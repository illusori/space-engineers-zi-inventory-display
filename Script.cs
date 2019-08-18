string _script_name = "Zephyr Industries Inventory Display";
string _script_version = "1.4.6";

string _script_title = null;
string _script_title_nl = null;

const int INV_HISTORY     = 100;
const int LOAD_HISTORY    = 100;
const int CARGO_HISTORY   = 100;
const int BATTERY_HISTORY = 100;

const int INV_SAMPLES     = 10;
const int LOAD_SAMPLES    = 10;
const int CARGO_SAMPLES   = 10;
const int BATTERY_SAMPLES = 10;

const int CYCLES_TOP     = 0;
const int CYCLES_INV     = 1;
const int CYCLES_CARGO   = 2;
const int CYCLES_BATTERY = 3;
const int SIZE_CYCLES    = 4;

const int PANELS_DEBUG = 0;
const int PANELS_WARN  = 1;
const int PANELS_INV   = 2;
const int PANELS_CHART = 3;
const int SIZE_PANELS  = 4;

const int CHART_TIME              = 0;
const int CHART_POWER_STORED      = 1;
const int CHART_POWER_IN          = 2;
const int CHART_POWER_OUT         = 3;
const int CHART_CARGO_USED_MASS   = 4;
const int CHART_CARGO_USED_VOLUME = 5;
const int CHART_CARGO_FREE_VOLUME = 6;
const int SIZE_CHARTS             = 7;

Dictionary<string, int> _chart_names = new Dictionary<string, int>() {
    { "time",              CHART_TIME },
    { "power_stored",      CHART_POWER_STORED },
    { "power_in",          CHART_POWER_IN },
    { "power_out",         CHART_POWER_OUT },
    { "cargo_used_mass",   CHART_CARGO_USED_MASS },
    { "cargo_used_volume", CHART_CARGO_USED_VOLUME },
    { "cargo_free_volume", CHART_CARGO_FREE_VOLUME }
};

MyIni _ini = new MyIni();

List<string> _panel_tags = new List<string>(SIZE_PANELS) { "@DebugDisplay", "@WarningDisplay", "@InventoryDisplay", "@ChartDisplay" };

/* Genuine global state */
List<int> _cycles = new List<int>(SIZE_CYCLES);

List<List<IMyTextPanel>> _panels = new List<List<IMyTextPanel>>(SIZE_PANELS);
List<string> _panel_text = new List<string>(SIZE_PANELS) { "", "", "", "", "" };

List<Chart> _charts = new List<Chart>(SIZE_CHARTS);

string _inv_text = "", _cargo_text = ""; // FIXME: StringBuilder?

List<IMyTerminalBlock> _inventory_blocks = new List<IMyTerminalBlock>();
List<IMyCargoContainer> _cargo_blocks = new List<IMyCargoContainer>();
List<IMyBatteryBlock> _battery_blocks = new List<IMyBatteryBlock>();


class LoadSample {
    public long Load;
    public double Time;
}

class CargoSample {
    public MyFixedPoint UsedMass, UsedVolume, MaxVolume;
}

class BatterySample {
    public float CurrentStoredPower, MaxStoredPower, CurrentInput, MaxInput, CurrentOutput, MaxOutput;
}

List<LoadSample> _load = new List<LoadSample>(LOAD_HISTORY);
List<Dictionary<MyItemType, double>> _item_counts = new List<Dictionary<MyItemType, double>>(INV_HISTORY);
List<CargoSample> _cargo = new List<CargoSample>(CARGO_HISTORY);
List<BatterySample> _battery = new List<BatterySample>(BATTERY_HISTORY);


// panel.EntityId => DrawBuffer
Dictionary<long, DrawBuffer> _chart_buffers = new Dictionary<long, DrawBuffer>();

long load_sample_total = 0;
double time_sample_total = 0.0;
double time_total = 0.0;

/* Reused single-run state objects, only global to avoid realloc/gc-thrashing */
List<MyInventoryItem> items = new List<MyInventoryItem>();
IMyInventory inv = null;

public Program() {
    _script_title = $"{_script_name} v{_script_version}";
    _script_title_nl = $"{_script_name} v{_script_version}\n";

    for (int i = 0; i < SIZE_CYCLES; i++) {
        _cycles.Add(0);
    }
    for (int i = 0; i < SIZE_PANELS; i++) {
        _panels.Add(new List<IMyTextPanel>());
    }
    for (int i = 0; i < LOAD_HISTORY; i++) {
        _load.Add(new LoadSample());
    }
    for (int i = 0; i < INV_HISTORY; i++) {
        _item_counts.Add(new Dictionary<MyItemType, double>());
    }
    for (int i = 0; i < CARGO_HISTORY; i++) {
        _cargo.Add(new CargoSample());
    }
    for (int i = 0; i < BATTERY_HISTORY; i++) {
        _battery.Add(new BatterySample());
    }

    _charts.Add(new Chart("Exec Time", "us"));
    _charts.Add(new Chart("Stored Power", "MWh"));
    _charts.Add(new Chart("Power In", "MW"));
    _charts.Add(new Chart("Power Out", "MW"));
    _charts.Add(new Chart("Cargo Mass", "kt"));
    _charts.Add(new Chart("Cargo Vol", "m3"));
    _charts.Add(new Chart("Cargo Free", "m3"));

    FindPanels();
    FindInventoryBlocks();
    FindCargoBlocks();
    FindBatteryBlocks();

    if (!Me.CustomName.Contains(_script_name)) {
        // Update our block to include our script name
        Me.CustomName = $"{Me.CustomName} - {_script_name}";
    }
    Log(_script_title);

    Runtime.UpdateFrequency |= UpdateFrequency.Update100;
}

public void Save() {
}

public int SafeMod(int val, int mod) {
    while (val < 0)
        val += mod;
    return val % mod;
}

public int InvOffset(int delta)     { return SafeMod(_cycles[CYCLES_INV] + delta, INV_HISTORY); }
public int LoadOffset(int delta)    { return SafeMod(_cycles[CYCLES_TOP] + delta, LOAD_HISTORY); }
public int CargoOffset(int delta)   { return SafeMod(_cycles[CYCLES_CARGO] + delta, CARGO_HISTORY); }
public int BatteryOffset(int delta) { return SafeMod(_cycles[CYCLES_BATTERY] + delta, BATTERY_HISTORY); }

public void Main(string argument, UpdateType updateSource) {
    try {
        if ((updateSource & UpdateType.Update100) != 0) {
	    //DateTime start_time = DateTime.Now;
            // FIXME: System.Diagnostics.Stopwatch
            // Runtime.LastRunTimeMs
            // Runtime.TimeSinceLastRun

	    _cycles[CYCLES_TOP]++;

	    _load[LoadOffset(-1)].Time = Runtime.LastRunTimeMs;
            if (_cycles[CYCLES_TOP] > 1) {
                time_total += Runtime.LastRunTimeMs;
                if (_cycles[CYCLES_TOP] == 201) {
                    Warning($"Total time after 200 cycles: {time_total}ms.");
                }
            }

            ClearPanels(PANELS_DEBUG);

            Log(_script_title_nl);

            if ((_cycles[CYCLES_TOP] % 30) == 0) {
                FindPanels();
            }
            if ((_cycles[CYCLES_TOP] % 30) == 1) {
                FindInventoryBlocks();
            }
            if ((_cycles[CYCLES_TOP] % 30) == 2) {
                FindCargoBlocks();
            }
            if ((_cycles[CYCLES_TOP] % 30) == 3) {
                FindBatteryBlocks();
            }

            UpdateInventoryStats();
            UpdateCargoStats();
            UpdateBatteryStats();

            UpdateInventoryText();
            UpdateCargoText();
            CompositeInventoryPanel();

            FlushToPanels(PANELS_INV);

	    ResetChartBuffers();
	    UpdateTimeChart();
	    UpdatePowerCharts();
	    UpdateCargoCharts();
	    FlushChartBuffers();

	    _load[LoadOffset(0)].Load = Runtime.CurrentInstructionCount;
	    //_load[LoadOffset(0)].Time = (DateTime.Now - start_time).Ticks;

            load_sample_total = load_sample_total - _load[LoadOffset(-LOAD_SAMPLES - 1)].Load + _load[LoadOffset(0)].Load;
            time_sample_total = time_sample_total - _load[LoadOffset(-LOAD_SAMPLES - 2)].Time + _load[LoadOffset(-1)].Time;
	    long load_avg = load_sample_total / (long)LOAD_SAMPLES;
	    long time_avg = TimeAsUsec(time_sample_total) / (long)LOAD_SAMPLES;
	    Log($"Load avg {load_avg}/{Runtime.MaxInstructionCount} in {time_avg}us");

            // Start at T-1 - exec time hasn't been updated yet.
            for (int i = 1; i < 16; i++) {
                long load = _load[LoadOffset(-i)].Load;
                long time = TimeAsUsec(_load[LoadOffset(-i)].Time);
                Log($"  [T-{i,-2}] Load {load} in {time}us");
            }
            Log($"Charts: {_charts.Count}, DrawBuffers: {_chart_buffers.Count}");
            FlushToPanels(PANELS_DEBUG);
        }
    } catch (Exception e) {
        string mess = $"An exception occurred during script execution.\nException: {e}\n---";
        Log(mess);
        Warning(mess);
        FlushToPanels(PANELS_DEBUG);
        throw;
    }
}

public long TimeAsUsec(double t) {
    //return (t * 1000L) / TimeSpan.TicksPerMillisecond;
    return (long)(t * 1000.0);
}

public void UpdateInventoryStats() {
    _cycles[CYCLES_INV]++;

    //Log("Boop!");
    int last = InvOffset(-INV_SAMPLES), current = InvOffset(0);
    Log($"[Inv run {_cycles[CYCLES_INV]}] Offsets: last {last}, current {current}");

    _item_counts[current].Clear();
    int num_invs = 0;
    //string item_name;
    double existing;
    MyInventoryItem item;
    foreach (IMyTerminalBlock inventory_block in _inventory_blocks) {
        if (inventory_block == null) {
            //Log("Block is null.");
            continue;
        }
        //Log("GetInv");
        for (int i = 0, sz = inventory_block.InventoryCount; i < sz; i++) {
            num_invs++;
            inv = inventory_block.GetInventory(i);
            if (inv == null) {
                //Log("Block has null inventory.");
            }
            //Log("GetItems");
            items.Clear();
            inv.GetItems(items);
            //Log("GotItems");
            if (items == null) {
                //Log("No items found.");
                continue;
            }
            //Log("item in items");
            //item_name = null;
            existing = 0.0;
            for (int j = 0, szj = items.Count; j < szj; j++) {
            //foreach (MyInventoryItem item in items) {
                item = items[j];
                if (item == null) {
                    //Log("Found null item");
                    continue;
                }
                //Log($"Found {item.Type.TypeId} {item.Type.SubtypeId} {item.Amount}");
                //Log($"Found {item.Type.SubtypeId} {item.Amount}");
                // FIXME: expand item name on display, not inside loop
                //item_name = GetItemName(item.Type);
                existing = 0.0;
                _item_counts[current].TryGetValue(item.Type, out existing);
                _item_counts[current][item.Type] = existing + (double)item.Amount;
            }
        }
    }
    Log($"  {num_invs} inventories in {_inventory_blocks.Count} blocks.");
}

public void UpdateCargoStats() {
    _cycles[CYCLES_CARGO]++;

    int last = CargoOffset(-CARGO_SAMPLES), current = CargoOffset(0);
    CargoSample sample = _cargo[current];

    sample.UsedMass   = (MyFixedPoint)0.0;
    sample.UsedVolume = (MyFixedPoint)0.0;
    sample.MaxVolume  = (MyFixedPoint)0.0;
    int num_invs = 0;
    foreach (IMyCargoContainer cargo_block in _cargo_blocks) {
        if (cargo_block == null) {
            //Log("Block is null.");
            continue;
        }
        for (int i = 0, sz = cargo_block.InventoryCount; i < sz; i++) {
            num_invs++;
            inv = cargo_block.GetInventory(i);
            if (inv == null) {
                //Log("Block has null inventory.");
            }
            sample.UsedMass   = MyFixedPoint.AddSafe(sample.UsedMass,   inv.CurrentMass);
            sample.UsedVolume = MyFixedPoint.AddSafe(sample.UsedVolume, inv.CurrentVolume);
            sample.MaxVolume  = MyFixedPoint.AddSafe(sample.MaxVolume,  inv.MaxVolume);
        }
    }
    Log($"  {num_invs} inventories in {_cargo_blocks.Count} cargoes.");
}

void UpdateInventoryText() {
    int last = InvOffset(-INV_SAMPLES), current = InvOffset(0);
    double old, value;
    int delta;
    string item_name;
    _inv_text = "";
    foreach (KeyValuePair<MyItemType, double> kvp in _item_counts[current].OrderBy(key => -key.Value)) {
        item_name = GetItemName(kvp.Key);
        value = kvp.Value;
        old = 0.0;
        _item_counts[last].TryGetValue(kvp.Key, out old);
        delta = (int)(value - old) / INV_SAMPLES;
        _inv_text += $"{(int)value,8} {item_name}{delta,0:' ['+#']';' ['-#']';''}\n";
    }
}

void UpdateCargoText() {
    int last = InvOffset(-CARGO_SAMPLES), current = InvOffset(0);
    CargoSample sample = _cargo[current], last_sample = _cargo[last];

    MyFixedPoint free_volume = MyFixedPoint.AddSafe(sample.MaxVolume, -sample.UsedVolume);

    int delta_used_mass   = (int)((double)MyFixedPoint.AddSafe(sample.UsedMass,   -last_sample.UsedMass) / CARGO_SAMPLES);
    int delta_used_volume = (int)((double)MyFixedPoint.AddSafe(sample.UsedVolume, -last_sample.UsedVolume) / CARGO_SAMPLES);
    int delta_max_volume  = (int)((double)MyFixedPoint.AddSafe(sample.MaxVolume,  -last_sample.MaxVolume) / CARGO_SAMPLES);
    int delta_free_volume = delta_max_volume - delta_used_volume;

    //_cargo_text = $"     Mass      Volume       Free\n{(int)sample.UsedMass,10}kg {(int)sample.UsedVolume,5}/{(int)sample.MaxVolume,5}m3 {(int)free_volume,5}m3\n{delta_used_mass,10:+#;-#;0}kg {delta_used_volume,5:+#;-#;0}/{delta_max_volume,5:+#;-#;0}m3 {delta_free_volume,5:+#;-#;0}m3\n";
     _cargo_text = $"      Mass      Volume       Free\n{(int)sample.UsedMass,10}kg {(int)sample.UsedVolume,5}/{(int)sample.MaxVolume,5}m3 {(int)free_volume,5}m3\n{delta_used_mass,12:+#'kg';-#'kg';''} {delta_used_volume,5:+#;-#;''}/{delta_max_volume,7:+#'m3';-#'m3';''} {delta_free_volume,7:+#'m3';-#'m3';''}\n";
}

void CompositeInventoryPanel() {
    ClearPanels(PANELS_INV);
    WritePanels(PANELS_INV, $"{_script_title_nl}\n{_cargo_text}\n{_inv_text}");
}

public void UpdateBatteryStats() {
    _cycles[CYCLES_BATTERY]++;

    int last = BatteryOffset(-BATTERY_SAMPLES), current = BatteryOffset(0);
    BatterySample sample = _battery[current];

    sample.CurrentStoredPower  = 0.0F;
    sample.MaxStoredPower = 0.0F;
    sample.CurrentInput  = 0.0F;
    sample.MaxInput  = 0.0F;
    sample.CurrentOutput  = 0.0F;
    sample.MaxOutput  = 0.0F;

    int num_batteries = 0, num_recharge = 0, num_discharge = 0;
    foreach (IMyBatteryBlock battery_block in _battery_blocks) {
        if (battery_block == null) {
            //Log("Block is null.");
            continue;
        }
        num_batteries++;
        sample.CurrentStoredPower += battery_block.CurrentStoredPower;
        sample.MaxStoredPower += battery_block.MaxStoredPower;
        sample.CurrentInput += battery_block.CurrentInput;
        sample.CurrentOutput += battery_block.CurrentOutput;
        // Add to MaxInput if not discharge-only, add to MaxOutput if not recharge-only
        if (battery_block.ChargeMode == ChargeMode.Recharge) {
            num_recharge++;
        } else {
            sample.MaxOutput += battery_block.MaxOutput;
        }
        if (battery_block.ChargeMode == ChargeMode.Discharge) {
            num_discharge++;
        } else {
            sample.MaxInput += battery_block.MaxInput;
        }
    }
    Log($"  {num_batteries} batteries with {num_recharge} recharging and {num_discharge} discharging.");
}


public string GetItemName(MyItemType item_type) {
    if (item_type.TypeId == "MyObjectBuilder_Ingot") {
        if (item_type.SubtypeId == "Stone")
            return "Gravel";
        return $"{item_type.SubtypeId} Ingot";
    }
    if (item_type.TypeId == "MyObjectBuilder_Ore") {
        if (item_type.SubtypeId == "Stone" || item_type.SubtypeId == "Ice")
            return item_type.SubtypeId;
        return $"{item_type.SubtypeId} Ore";
    }
    if (item_type.TypeId == "MyObjectBuilder_Component") {
        return item_type.SubtypeId;
    }
    if (item_type.TypeId == "MyObjectBuilder_PhysicalGunObject") {
        return item_type.SubtypeId;
    }
    if (item_type.TypeId == "MyObjectBuilder_GasContainerObject") {
        return item_type.SubtypeId;
    }
    if (item_type.TypeId == "MyObjectBuilder_OxygenContainerObject") {
        return item_type.SubtypeId;
    }
    if (item_type.TypeId == "MyObjectBuilder_AmmoMagazine") {
        return item_type.SubtypeId;
    }
    return $"{item_type.TypeId} {item_type.SubtypeId}";
}

public void FindPanels() {
    for (int i = 0; i < SIZE_PANELS; i++) {
        _panels[i].Clear();
        GridTerminalSystem.GetBlocksOfType<IMyTextPanel>(_panels[i], block => block.CustomName.Contains(_panel_tags[i]));
        for (int j = 0, szj = _panels[i].Count; j < szj; j++) {
            _panels[i][j].ContentType = ContentType.TEXT_AND_IMAGE;
            _panels[i][j].Font = "Monospace";
            _panels[i][j].FontSize = 0.5F;
            _panels[i][j].TextPadding = 0.5F;
            _panels[i][j].Alignment = TextAlignment.LEFT;
        }
    }
    // Special logic for ChartPanels, need to set up buffers and read their config.
    HashSet<long> found_ids = new HashSet<long>(_panels[PANELS_CHART].Count);
    DrawBuffer buffer;

    MyIniParseResult parse_result;
    List<string> sections = new List<string>();
    int chart_kind, width, height, x, y;
    bool horizontal, show_title, show_cur, show_avg, show_max, show_scale;
    string name;
    for (int i = 0, sz = _panels[PANELS_CHART].Count; i < sz; i++) {
        IMyTextPanel panel = _panels[PANELS_CHART][i];
        long id = panel.EntityId;
        found_ids.Add(id);
        if (!_chart_buffers.TryGetValue(id, out buffer)) {
            // 42x28 seems about right for 1x1 panel at 0.6
            // 52x35 for 1x1 panel at 0.5 with 0.5% padding.
            // 1x1 panel is 512 wide, 2x1 presumeably 1024 wide.
            float scale = panel.SurfaceSize.X / 512F;
            buffer = new DrawBuffer(panel, (int)(52F * scale), 35);
            _chart_buffers.Add(id, buffer);
        } else {
            if (panel.CustomData.GetHashCode() == buffer.ConfigHash) {
                //Warning($"Chart panel skipping unchanged config parse on panel '{panel.CustomName}'");
                continue;
            }
            buffer.Clear();
            buffer.Save();
        }
        buffer.ConfigHash = panel.CustomData.GetHashCode();
        if (!_ini.TryParse(panel.CustomData, out parse_result)) {
            Warning($"Chart panel parse error on panel '{panel.CustomName}' line {parse_result.LineNo}: {parse_result.Error}");
            found_ids.Remove(id); // Move along. Nothing to see. Pretend we never saw the panel.
            continue;
        }
        _ini.GetSections(sections);
        foreach (string section in sections) {
            //Warning($"Found section {section}");
            name = _ini.Get(section, "chart").ToString(section);
            if (!_chart_names.TryGetValue(name, out chart_kind)) {
                Warning($"Chart panel '{panel.CustomName}' error in section '{section}': '{name}' is not the name of a known chart type."); // FIXME: list chart names
                continue;
            }
            width = _ini.Get(section, "width").ToInt32(buffer.X);
            height = _ini.Get(section, "height").ToInt32(buffer.Y);
            x = _ini.Get(section, "x").ToInt32(0);
            y = _ini.Get(section, "y").ToInt32(0);
            // horizontal, etc ChartOptions settings.
            horizontal = _ini.Get(section, "horizontal").ToBoolean(true);
            show_title = _ini.Get(section, "show_title").ToBoolean(true);
            show_cur = _ini.Get(section, "show_cut").ToBoolean(true);
            show_avg = _ini.Get(section, "show_avg").ToBoolean(true);
            show_max = _ini.Get(section, "show_max").ToBoolean(false);
            show_scale = _ini.Get(section, "show_scale").ToBoolean(true);

            // Hmm, removing it here means we can't have multiples of same chart on same panel
            // TODO: maybe keep track of those chart_kind we've removed already in the sections loop?
            _charts[chart_kind].RemoveDisplaysForBuffer(buffer);
    	    _charts[chart_kind].AddBuffer(buffer, x, y, width, height, new ChartOptions(horizontal, show_title, show_cur, show_avg, show_max, show_scale));
        }
    }
    // Prune old ids in _chart_buffers
    HashSet<long> old_ids = new HashSet<long>(_chart_buffers.Keys);
    old_ids.ExceptWith(found_ids);
    foreach (long missing_id in old_ids) {
        foreach (Chart chart in _charts) {
            chart.RemoveDisplaysForBuffer(_chart_buffers[missing_id]);
        }
        _chart_buffers.Remove(missing_id);
    }
}

public void FindInventoryBlocks() {
    _inventory_blocks.Clear();
    GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(_inventory_blocks, block => block.HasInventory);
    //Log($"Found {_inventory_blocks.Count} inventory blocks.");
}

public void FindCargoBlocks() {
    _cargo_blocks.Clear();
    GridTerminalSystem.GetBlocksOfType<IMyCargoContainer>(_cargo_blocks, block => block.HasInventory);
    //Log($"Found {_cargo_blocks.Count} cargo blocks.");
}

public void FindBatteryBlocks() {
    _battery_blocks.Clear();
    GridTerminalSystem.GetBlocksOfType<IMyBatteryBlock>(_battery_blocks);
    //Log($"Found {_battery_blocks.Count} battery blocks.");
}

/*
public void FindPowerProducerBlocks() {
    _power_blocks.Clear();
    GridTerminalSystem.GetBlocksOfType<IMyPowerProducer>(_power_blocks);
    //Log($"Found {_power_blocks.Count} power-producing blocks.");
}
 */

public void ClearAllPanels() {
    for (int i = 0; i < SIZE_PANELS; i++) {
        ClearPanels(i);
    }
}

public void ClearPanels(int kind) {
    _panel_text[kind] = "";
}

// FIXME: use System.Text.StringBuilder?
// StringBuilder.Clear() or StringBuilder.Length = 0
// new StringBuilder("", capacity);
// StringBuilder.Append(s)
// StringBuilder.ToString()
public void WritePanels(int kind, string s) {
    _panel_text[kind] += s;
}

public void FlushToAllPanels() {
    for (int i = 0; i < SIZE_PANELS; i++) {
        FlushToPanels(i);
    }
}

/*
IMyTextSurface textSurface = block.GetSurface(i);
frame = textSurface.DrawFrame();
sprite = MySprite.CreateText(string text, string fontId, Color color, float fontSize, TextAlignment textAlignment);
sprite.Position = new Vector2(textSurface.TextPadding, textSurface.TextPadding);
frame.Add(sprite);
 */
public void FlushToPanels(int kind) {
    for (int i = 0, sz = _panels[kind].Count; i < sz; i++) {
        if (_panels[kind][i] != null) {
            _panels[kind][i].WriteText(_panel_text[kind], false);
        }
    }
}

public void Log(string s) {
    WritePanels(PANELS_DEBUG, s + "\n");
    Echo(s);
}

public void Warning(string s) {
    WritePanels(PANELS_WARN, $"[{DateTime.Now,11:HH:mm:ss.ff}] {s}\n");
    FlushToPanels(PANELS_WARN); // Never clear buffer and and always immediately flush.
}

public void ResetChartBuffers() {
    foreach (DrawBuffer buffer in _chart_buffers.Values) {
        buffer.Reset();
    }
}

public void FlushChartBuffers() {
    foreach (DrawBuffer buffer in _chart_buffers.Values) {
        buffer.Flush();
    }
}

public void UpdateTimeChart() {
    if (_charts[CHART_TIME].IsViewed) {
        double max = TimeAsUsec(_load.Max(x => x.Time));
        _charts[CHART_TIME].StartDraw();
        for (int i = 0; i < LOAD_HISTORY - 1; i++) {
            //Log($"Update T-{i,-2}");
            // Start at -1 because "now" hasn't been updated yet.
            _charts[CHART_TIME].DrawBar(i, TimeAsUsec(_load[LoadOffset(-i - 1)].Time), max);
        }
        _charts[CHART_TIME].EndDraw();
    }
}

public void UpdatePowerCharts() {
    if (_charts[CHART_POWER_STORED].IsViewed || _charts[CHART_POWER_IN].IsViewed || _charts[CHART_POWER_OUT].IsViewed) {
        int current = BatteryOffset(0), then;
	double stored_max = (double)_battery[current].MaxStoredPower;
	double in_max = 0.0; //(double)_battery[current].MaxInput;
	double out_max = 0.0; //(double)_battery[current].MaxOutput;
	for (int i = 0; i < BATTERY_HISTORY; i++) {
            then = BatteryOffset(-i);
            if ((double)_battery[then].MaxStoredPower > stored_max)
                stored_max = (double)_battery[then].MaxStoredPower;
            if ((double)_battery[then].CurrentInput > in_max)
                in_max = (double)_battery[then].CurrentInput;
            if ((double)_battery[then].CurrentOutput > out_max)
                out_max = (double)_battery[then].CurrentOutput;
	}
        _charts[CHART_POWER_STORED].StartDraw();
        _charts[CHART_POWER_IN].StartDraw();
        _charts[CHART_POWER_OUT].StartDraw();
	for (int i = 0; i < BATTERY_HISTORY; i++) {
	    //Log($"Update T-{i,-2}");
            then = BatteryOffset(-i);
	    _charts[CHART_POWER_STORED].DrawBar(i, (double)_battery[then].CurrentStoredPower, (double)stored_max);
	    _charts[CHART_POWER_IN].DrawBar(i, (double)_battery[then].CurrentInput, (double)in_max);
	    _charts[CHART_POWER_OUT].DrawBar(i, (double)_battery[then].CurrentOutput, (double)out_max);
	}
        _charts[CHART_POWER_STORED].EndDraw();
        _charts[CHART_POWER_IN].EndDraw();
        _charts[CHART_POWER_OUT].EndDraw();
    }
}

public void UpdateCargoCharts() {
    if (_charts[CHART_CARGO_USED_MASS].IsViewed || _charts[CHART_CARGO_USED_VOLUME].IsViewed || _charts[CHART_CARGO_FREE_VOLUME].IsViewed) {
        int current = CargoOffset(0), then;
	double max_volume = (double)_cargo[current].MaxVolume;
	double used_mass_max = 0.0;
	double used_volume_max = 0.0;
	double free_volume_max = 0.0;
        double val;
	for (int i = 0; i < CARGO_HISTORY; i++) {
            then = CargoOffset(-i);
            val = (double)_cargo[then].UsedMass;
            if (val > used_mass_max)
                used_mass_max = val;
            val = (double)_cargo[then].UsedVolume;
            if (val > used_volume_max)
                used_volume_max = val;
            val = (double)_cargo[then].MaxVolume - (double)_cargo[then].UsedVolume;
            if (val > free_volume_max)
                free_volume_max = val;
	}
        _charts[CHART_CARGO_USED_MASS].StartDraw();
        _charts[CHART_CARGO_USED_VOLUME].StartDraw();
        _charts[CHART_CARGO_FREE_VOLUME].StartDraw();
	for (int i = 0; i < CARGO_HISTORY; i++) {
	    //Log($"Update T-{i,-2}");
            then = CargoOffset(-i);
	    _charts[CHART_CARGO_USED_MASS].DrawBar(i, (double)_cargo[then].UsedMass / 1000000.0, (double)used_mass_max / 1000000.0);
	    _charts[CHART_CARGO_USED_VOLUME].DrawBar(i, (double)_cargo[then].UsedVolume, (double)used_volume_max);
	    _charts[CHART_CARGO_FREE_VOLUME].DrawBar(i, (double)_cargo[then].MaxVolume - (double)_cargo[then].UsedVolume, (double)free_volume_max);
	}
        _charts[CHART_CARGO_USED_MASS].EndDraw();
        _charts[CHART_CARGO_USED_VOLUME].EndDraw();
        _charts[CHART_CARGO_FREE_VOLUME].EndDraw();
    }
}

public class DrawBuffer {
    public IMyTextPanel Panel;
    public int X, Y;
    public List<StringBuilder> Buffer;
    public int ConfigHash;
    private List<string> template;
    string blank;

    public DrawBuffer(IMyTextPanel panel, int x, int y) {
        Panel = panel;
        X = x;
        Y = y;
        blank = new String(' ', X) + "\n";
        Buffer = new List<StringBuilder>(Y);
        template = new List<string>(Y);
        for (int i = 0; i < Y; i++) {
            Buffer.Add(new StringBuilder(blank));
            template.Add(blank);
        }
        ConfigHash = 0;
    }

    public void Save() {
        for (int i = 0; i < Y; i++) {
            template[i] = Buffer[i].ToString();
        }
    }

    public void Reset() {
        for (int i = 0; i < Y; i++) {
            Buffer[i].Clear().Append(template[i]);
        }
    }

    public void Clear() {
        for (int i = 0; i < Y; i++) {
            Buffer[i].Clear().Append(blank);
        }
    }

    public void Write(int x, int y, string s) {
        if (s.Length == 1) {
            Buffer[y][x] = s[0];
        } else {
            Buffer[y].Remove(x, s.Length).Insert(x, s);
        }
    }

    public void Flush() {
        if (Panel != null) {
            Panel.WriteText(ToString(), false);
        }
    }

    override public string ToString() {
        return string.Concat(Buffer);
    }
}

public class ViewPort {
    public DrawBuffer Buffer;
    public int offsetX, offsetY;
    public int X, Y;

    public ViewPort(DrawBuffer buffer, int offset_x, int offset_y, int x, int y) {
        Buffer = buffer;
        offsetX = offset_x;
        offsetY = offset_y;
        X = x;
        Y = y;
    }

    public void Save() {
        Buffer.Save();
    }

    public void Write(int x, int y, string s) {
        // Yeah, no bounds checking. Sue me.
        Buffer.Write(offsetX + x, offsetY + y, s);
    }
}

public class ChartOptions {
    public bool Horizontal, ShowTitle, ShowCur, ShowAvg, ShowMax, ShowScale;

    public ChartOptions(bool horizontal = true, bool show_title = true, bool show_cur = true, bool show_avg = true, bool show_max = false, bool show_scale = true) {
        Horizontal = horizontal;
        ShowTitle = show_title;
        ShowCur = show_cur;
        ShowAvg = show_avg;
        ShowMax = show_max;
        ShowScale = show_scale;
    }
}

public class ChartDisplay {
    public ViewPort Viewport;
    public ChartOptions Options;

    public int? CurOffset = 0, AvgOffset = 0, MaxOffset = 0, ScaleOffset = 0;
    public double? SampleCur;
    public double SampleTotal, SampleMax, Scale;
    public int NumSamples;

    public ChartDisplay(ViewPort viewport, ChartOptions options) {
        Viewport = viewport;
        Options = options;
    }
}

public class Chart {
    static List<string> _y_blocks = new List<string>(8) {
      " ", "\u2581", "\u2582", "\u2583", "\u2584", "\u2585", "\u2586", "\u2587", "\u2588",
    };
    static List<string> _x_blocks = new List<string>(8) {
      " ", "\u258F", "\u258E", "\u258D", "\u258C", "\u258B", "\u258A", "\u2589", "\u2588",
    };

    private List<ChartDisplay> displays;

    public string Title;
    public string Unit;

    public bool IsViewed { get { return displays.Count > 0; } }

    public Chart(string title, string unit) {
        displays = new List<ChartDisplay>();
        Title = title;
        Unit = unit;
    }

    public void AddViewPort(ViewPort viewport, ChartOptions options) {
        string label;
        ChartDisplay display = new ChartDisplay(viewport, options);

        displays.Add(display);

	viewport.Write(0, 0, "." + new String('-', viewport.X - 2) + ".");
        if (options.ShowTitle) {
            label = $"[{Title}]";
            if (label.Length < viewport.X - 2) {
                viewport.Write((viewport.X - label.Length) / 2, 0, label);
            }
        }
	for (int i = 1; i < viewport.Y - 1; i++) {
	    viewport.Write(0, i, "|");
	    viewport.Write(viewport.X - 1, i, "|");
	}
	viewport.Write(0, viewport.Y - 1, "." + new String('-', viewport.X - 2) + ".");
        if (options.ShowCur || options.ShowAvg || options.ShowMax || options.ShowScale) {
            List<string> segments = new List<string>(2);
            int offset = 1;
            if (options.ShowCur) {
                label = $"cur:     {Unit}";
                segments.Add(label);
                display.CurOffset = offset + 4;
                offset += label.Length + 1;
            }
            if (options.ShowAvg) {
                label = $"avg:     {Unit}";
                segments.Add(label);
                display.AvgOffset = offset + 4;
                offset += label.Length + 1;
            }
            if (options.ShowMax) {
                label = $"max:     {Unit}";
                segments.Add(label);
                display.MaxOffset = offset + 4;
                offset += label.Length + 1;
            }
            if (options.ShowScale) {
                string dim = options.Horizontal ? "Y" : "X";
                label = $"{dim}:     {Unit}";
                segments.Add(label);
                display.ScaleOffset = offset + 2;
                offset += label.Length + 1;
            }
            label = "[" + string.Join(" ", segments) + "]";
            if (label.Length < viewport.X - 2) {
                offset = (viewport.X - label.Length) / 2;
                viewport.Write(offset, viewport.Y - 1, label);
                display.CurOffset += offset;
                display.AvgOffset += offset;
                display.MaxOffset += offset;
                display.ScaleOffset += offset;
            } else {
                display.CurOffset = null;
                display.AvgOffset = null;
                display.MaxOffset = null;
                display.ScaleOffset = null;
            }
        }
	viewport.Save();
    }

    public void AddBuffer(DrawBuffer buffer, int offset_x, int offset_y, int x, int y, ChartOptions options) {
        AddViewPort(new ViewPort(buffer, offset_x, offset_y, x, y), options);
    }

    public void AddBuffer(DrawBuffer buffer, int offset_x, int offset_y, int x, int y) {
        AddViewPort(new ViewPort(buffer, offset_x, offset_y, x, y), new ChartOptions());
    }

    public void RemoveDisplays() {
        displays.Clear();
    }

    public void RemoveDisplaysForBuffer(DrawBuffer buffer) {
        displays.RemoveAll(display => display.Viewport.Buffer == buffer);
    }

    private void DrawBarToDisplay(int d, int t, double val, double max) {
        ChartDisplay display = displays[d];
        ViewPort viewport = display.Viewport;
        ChartOptions options = display.Options;
	int x, y, size, dx, dy;
	List<string> blocks;

        if (options.Horizontal) {
	    x = viewport.X - 2 - t;
            if (x < 1)
                return;
            y = viewport.Y - 2;
	    size = viewport.Y - 2;
	    blocks = _y_blocks;
	    dx = 0;
            dy = -1;
        } else {
	    x = 1;
            y = viewport.Y - 2 - t;
            if (y < 1)
                return;
	    size = viewport.X - 2;
	    blocks = _x_blocks;
	    dx = 1;
            dy = 0;
        }

        if (!display.SampleCur.HasValue)
            display.SampleCur = val;
        display.SampleTotal += val;
        if (val > display.SampleMax)
            display.SampleMax = val;
        display.Scale = max;
        display.NumSamples++;

        //parent.Log($"DrawBarToVP t{t}, v{val}, m{max}\nx{x}, y{y}, s{size}, dx{dx}, dy{dy}");

	double scaled = val * size / max;
	int repeat = (int)scaled;
	int fraction = (int)((scaled * 8.0) % (double)size);

	fraction = fraction >= 4 ? 4 : 0; // Only half block is implemented in font.

        // FIXME: unroll x/y versions and i < repeat and i == repeat cases.
	for (int i = 0; i < size; i++) {
	    if (i < repeat) {
                viewport.Buffer.Buffer[viewport.offsetY + y][viewport.offsetX + x] = blocks[8][0];
		//viewport.Write(x, y, blocks[8]); // TODO: unroll buffer write?
	    } else if (i == repeat) {
                viewport.Buffer.Buffer[viewport.offsetY + y][viewport.offsetX + x] = blocks[fraction][0];
		//viewport.Write(x, y, blocks[fraction]); // TODO: unroll buffer write?
		break;
	    }
	    x += dx;
	    y += dy;
	}
    }

    public void DrawBar(int t, double val, double max) {
        for (int d = 0, sz = displays.Count; d < sz; d++) {
            DrawBarToDisplay(d, t, val, max);
        }
    }

    public void StartDraw() {
        for (int d = 0, sz = displays.Count; d < sz; d++) {
            displays[d].SampleCur   = null;
            displays[d].SampleTotal = 0.0;
            displays[d].SampleMax   = 0.0;
            displays[d].Scale       = 0.0;
            displays[d].NumSamples  = 1;
        }
    }

    public void EndDraw() {
        float avg;
        ChartDisplay display;
        ViewPort viewport;
        ChartOptions options;
        for (int d = 0, sz = displays.Count; d < sz; d++) {
            display = displays[d];
            viewport = display.Viewport;
            options = display.Options;
            avg = (float)display.SampleTotal / (float)display.NumSamples;
            if (options.ShowCur && display.CurOffset.HasValue && display.SampleCur.HasValue) {
                viewport.Write((int)display.CurOffset, viewport.Y - 1, $"{display.SampleCur,5:G4}");
            }
            if (options.ShowAvg && display.AvgOffset.HasValue) {
                viewport.Write((int)display.AvgOffset, viewport.Y - 1, $"{avg,5:G4}");
            }
            if (options.ShowMax && display.MaxOffset.HasValue) {
                viewport.Write((int)display.MaxOffset, viewport.Y - 1, $"{display.SampleMax,5:G4}");
            }
            if (options.ShowScale && display.ScaleOffset.HasValue) {
                viewport.Write((int)display.ScaleOffset, viewport.Y - 1, $"{display.Scale,5:G4}");
            }
        }
    }
}
