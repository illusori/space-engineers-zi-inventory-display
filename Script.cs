string _script_name = "Zephyr Industries Inventory Display";
string _script_version = "1.4.1";

string _script_title = null;
string _script_title_nl = null;

const int INV_HISTORY     = 100;
const int LOAD_HISTORY    = 100;
const int TIME_HISTORY    = 100;
const int CARGO_HISTORY   = 100;
const int BATTERY_HISTORY = 100;

const int INV_SAMPLES     = 10;
const int LOAD_SAMPLES    = 10;
const int TIME_SAMPLES    = 10;
const int CARGO_SAMPLES   = 10;
const int BATTERY_SAMPLES = 10;

const int CYCLES_TOP     = 0;
const int CYCLES_INV     = 1;
const int CYCLES_CARGO   = 2;
const int CYCLES_BATTERY = 3;
const int SIZE_CYCLES    = 4;

const int PANELS_DEBUG = 0;
const int PANELS_INV   = 1;
const int PANELS_CHART = 2;
const int SIZE_PANELS  = 3;

const int CHART_TIME         = 0;
const int CHART_POWER_STORED = 1;
const int CHART_POWER_IN     = 2;
const int CHART_POWER_OUT    = 3;
const int SIZE_CHARTS        = 4;

List<string> _panel_tags = new List<string>(SIZE_PANELS) { "@DebugDisplay", "@InventoryDisplay", "@ChartDisplay" };

/* Genuine global state */
List<int> _cycles = new List<int>(SIZE_CYCLES);

List<List<IMyTextPanel>> _panels = new List<List<IMyTextPanel>>(SIZE_PANELS);
List<string> _panel_text = new List<string>(SIZE_PANELS) { "", "", "" };

List<Chart> _charts = new List<Chart>(SIZE_CHARTS);

string _inv_text = "", _cargo_text = ""; // FIXME: StringBuilder?

List<IMyTerminalBlock> _inventory_blocks = new List<IMyTerminalBlock>();
List<IMyCargoContainer> _cargo_blocks = new List<IMyCargoContainer>();
List<IMyBatteryBlock> _battery_blocks = new List<IMyBatteryBlock>();

List<long> _load = new List<long>();
List<long> _time = new List<long>();
List<Dictionary<string, MyFixedPoint>> _item_counts = new List<Dictionary<string, MyFixedPoint>>(INV_HISTORY);

// panel.EntityId => DrawBuffer
Dictionary<long, DrawBuffer> _chart_buffers = new Dictionary<long, DrawBuffer>();

class CargoSample {
    public MyFixedPoint UsedMass, UsedVolume, MaxVolume;
}

List<CargoSample> _cargo = new List<CargoSample>(CARGO_HISTORY);

class BatterySample {
    public float CurrentStoredPower, MaxStoredPower, CurrentInput, MaxInput, CurrentOutput, MaxOutput;
}

List<BatterySample> _battery = new List<BatterySample>(BATTERY_HISTORY);

/* Reused single-run state objects, only global to avoid realloc/gc-thrashing */
List<MyInventoryItem> items = new List<MyInventoryItem>();
IMyInventory inv = null;
MyFixedPoint existing = (MyFixedPoint)0.0;

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
        _load.Add(0L);
    }
    for (int i = 0; i < TIME_HISTORY; i++) {
        _time.Add(0L);
    }
    for (int i = 0; i < INV_HISTORY; i++) {
        _item_counts.Add(new Dictionary<string, MyFixedPoint>());
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

    FindPanels();
    FindInventoryBlocks();
    FindCargoBlocks();
    FindBatteryBlocks();

    SetupTimeChart();
    SetupPowerChart();

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
public int TimeOffset(int delta)    { return SafeMod(_cycles[CYCLES_TOP] + delta, TIME_HISTORY); }
public int CargoOffset(int delta)   { return SafeMod(_cycles[CYCLES_CARGO] + delta, CARGO_HISTORY); }
public int BatteryOffset(int delta) { return SafeMod(_cycles[CYCLES_BATTERY] + delta, BATTERY_HISTORY); }

public void Main(string argument, UpdateType updateSource) {
    try {
        if ((updateSource & UpdateType.Update100) != 0) {
	    DateTime start_time = DateTime.Now;
            // FIXME: System.Diagnostics.Stopwatch

	    _cycles[CYCLES_TOP]++;

            ClearPanels(PANELS_DEBUG);

            Log(_script_title_nl);

            if ((_cycles[CYCLES_TOP] % 30) == 0) {
                FindPanels();
                FindInventoryBlocks();
                FindCargoBlocks();
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
            UpdatePowerChart();
            FlushChartBuffers();

	    _load[LoadOffset(0)] = Runtime.CurrentInstructionCount;
	    _time[TimeOffset(0)] = (DateTime.Now - start_time).Ticks;

            // FIXME: across SAMPLES not HISTORY
	    long load_avg = _load.Sum() / LOAD_HISTORY;
	    long time_avg = (_time.Sum() * 1000L) / (TIME_HISTORY * TimeSpan.TicksPerMillisecond);
	    Log($"Load avg {load_avg}/{Runtime.MaxInstructionCount} in {time_avg}us");

            for (int i = 0; i < 16; i++) {
                long load = _load[LoadOffset(-i)];
                long time = (_time[TimeOffset(-i)] * 1000L) / TimeSpan.TicksPerMillisecond;
                Log($"  [T-{i,-2}] Load {load} in {time}us");
            }
            FlushToPanels(PANELS_DEBUG);
        }
    } catch (Exception e) {
        Log("An exception occurred during script execution.");
        Log($"Exception: {e}\n---");
        FlushToPanels(PANELS_DEBUG);
        throw;
    }
}

public void UpdateInventoryStats() {
    _cycles[CYCLES_INV]++;

    //Log("Boop!");
    int last = InvOffset(-INV_SAMPLES), current = InvOffset(0);
    Log($"[Inv run {_cycles[CYCLES_INV]}] Offsets: last {last}, current {current}");

    _item_counts[current].Clear();
    int num_invs = 0;
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
            string item_name = null;
            foreach (MyInventoryItem item in items) {
                if (item == null) {
                    //Log("Found null item");
                    continue;
                }
                //Log($"Found {item.Type.TypeId} {item.Type.SubtypeId} {item.Amount}");
                //Log($"Found {item.Type.SubtypeId} {item.Amount}");
                item_name = GetItemName(item.Type);
                existing = (MyFixedPoint)0.0;
                _item_counts[current].TryGetValue(item_name, out existing);
                _item_counts[current][item_name] = MyFixedPoint.AddSafe(existing, item.Amount);
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
    MyFixedPoint old, value;
    int delta;
    _inv_text = "";
    foreach (KeyValuePair<string, MyFixedPoint> kvp in _item_counts[current]) {
        value = kvp.Value;
        old = (MyFixedPoint)0.0;
        _item_counts[last].TryGetValue(kvp.Key, out old);
        delta = (int)MyFixedPoint.AddSafe(value, old == null ? -value : -old) / INV_SAMPLES;
        _inv_text += $"{(int)value,8} {kvp.Key}{delta,0:' ['+#']';' ['-#']';''}\n";
        /*
        if (delta != 0) {
            _inv_text += $"{(int)value,8} {kvp.Key} [{delta,0:+#;-#;0}]\n";
        } else {
            _inv_text += $"{(int)value,8} {kvp.Key}\n";
        }
        */
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
            _panels[i][j].FontSize = 0.6F;
            _panels[i][j].Alignment = TextAlignment.LEFT;
        }
    }
    // Special logic for ChartPanels, need to set up buffers and read their config.
    HashSet<long> found_ids = new HashSet<long>(_panels[PANELS_CHART].Count);
    DrawBuffer buffer;
    for (int i = 0, sz = _panels[PANELS_CHART].Count; i < sz; i++) {
        IMyTextPanel panel = _panels[PANELS_CHART][i];
        long id = panel.EntityId;
        found_ids.Add(id);
        if (!_chart_buffers.TryGetValue(id, out buffer)) {
            // 42x28 seems about right for 1x1 panel at 0.6
            // FIXME: read panel size
            buffer = new DrawBuffer(panel, 42, 28);
            _chart_buffers.Add(id, buffer);
        }
        // FIXME: read config, if config has changed: add buffers to charts.

        _charts[CHART_POWER_STORED].RemoveDisplaysForBuffer(buffer);
        _charts[CHART_POWER_IN].RemoveDisplaysForBuffer(buffer);
        _charts[CHART_POWER_OUT].RemoveDisplaysForBuffer(buffer);

	_charts[CHART_POWER_STORED].AddBuffer(buffer, 0, 0, 42, 9);
	_charts[CHART_POWER_IN].AddBuffer(buffer, 0, 9, 42, 9);
	_charts[CHART_POWER_OUT].AddBuffer(buffer, 0, 18, 42, 9);
    }
    // FIXME: prune old ids in _chart_buffers
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

public void FlushToPanels(int kind) {
    foreach (IMyTextPanel panel in _panels[kind]) {
        if (panel != null) {
            panel.WriteText(_panel_text[kind], false);
        }
    }
}

public void Log(string s) {
    WritePanels(PANELS_DEBUG, s + "\n");
    Echo(s);
}

public void SetupTimeChart() {
    //_charts[CHART_TIME].AddBuffer(_chart_buffer, 0, 0, 42, 12);
    //_charts[CHART_TIME].AddBuffer(_chart_buffer, 0, 11, 21, 6);
    //_charts[CHART_TIME].AddBuffer(_chart_buffer, 20, 11, 22, 6);
}

public void SetupPowerChart() {
    //_charts[CHART_POWER_STORED].AddBuffer(_chart_buffer, 0, 0, 42, 9);
    //_charts[CHART_POWER_IN].AddBuffer(_chart_buffer, 0, 9, 42, 9);
    //_charts[CHART_POWER_OUT].AddBuffer(_chart_buffer, 0, 18, 42, 9);
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
    //_chart_buffer.Reset(); // FIXME: figure out where this goes

    if (_charts[CHART_TIME].IsViewed) {
        ClearPanels(PANELS_CHART);

        int max = (int)_time.Max();
        _charts[CHART_TIME].StartDraw();
        // Start at -1 because "now" hasn't been updated yet.
        for (int i = 0; i < TIME_HISTORY - 1; i++) {
            //Log($"Update T-{i,-2}");
            _charts[CHART_TIME].DrawBar(i, (int)_time[TimeOffset(-i - 1)], max);
        }
        _charts[CHART_TIME].EndDraw();

        //WritePanels(PANELS_CHART, _chart_buffer.ToString());
    }
}

public void UpdatePowerChart() {
    if (_charts[CHART_POWER_STORED].IsViewed || _charts[CHART_POWER_IN].IsViewed || _charts[CHART_POWER_OUT].IsViewed) {
        ClearPanels(PANELS_CHART);

        //_chart_buffer.Reset();
	int stored_max = (int)_battery[BatteryOffset(0)].MaxStoredPower;
	int in_max = 0; //(int)_battery[BatteryOffset(0)].MaxInput;
	int out_max = 0; //(int)_battery[BatteryOffset(0)].MaxOutput;
	for (int i = 0; i < BATTERY_HISTORY; i++) {
            if ((int)_battery[BatteryOffset(-i)].MaxStoredPower > stored_max)
                stored_max = (int)_battery[BatteryOffset(-i)].MaxStoredPower;
            if ((int)_battery[BatteryOffset(-i)].CurrentInput > in_max)
                in_max = (int)_battery[BatteryOffset(-i)].CurrentInput;
            if ((int)_battery[BatteryOffset(-i)].CurrentOutput > out_max)
                out_max = (int)_battery[BatteryOffset(-i)].CurrentOutput;
	}
        _charts[CHART_POWER_STORED].StartDraw();
        _charts[CHART_POWER_IN].StartDraw();
        _charts[CHART_POWER_OUT].StartDraw();
	for (int i = 0; i < BATTERY_HISTORY; i++) {
	    //Log($"Update T-{i,-2}");
	    _charts[CHART_POWER_STORED].DrawBar(i, (int)_battery[BatteryOffset(-i)].CurrentStoredPower, stored_max);
	    _charts[CHART_POWER_IN].DrawBar(i, (int)_battery[BatteryOffset(-i)].CurrentInput, in_max);
	    _charts[CHART_POWER_OUT].DrawBar(i, (int)_battery[BatteryOffset(-i)].CurrentOutput, out_max);
	}
        _charts[CHART_POWER_STORED].EndDraw();
        _charts[CHART_POWER_IN].EndDraw();
        _charts[CHART_POWER_OUT].EndDraw();

        //WritePanels(PANELS_CHART, _chart_buffer.ToString());
    }
}

public class DrawBuffer {
    public IMyTextPanel Panel;
    public int X, Y;
    public List<StringBuilder> Buffer;
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
    public DrawBuffer buffer;
    private int offsetX, offsetY;
    public int X, Y;

    public ViewPort(DrawBuffer buffer, int offset_x, int offset_y, int x, int y) {
        this.buffer = buffer;
        offsetX = offset_x;
        offsetY = offset_y;
        X = x;
        Y = y;
    }

    public void Save() {
        buffer.Save();
    }

    public void Write(int x, int y, string s) {
        // Yeah, no bounds checking. Sue me.
        buffer.Write(offsetX + x, offsetY + y, s);
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
    public ViewPort viewport;
    public ChartOptions options;

    public int CurOffset = 0, AvgOffset = 0, MaxOffset = 0, ScaleOffset = 0;
    public int SampleCur, SampleTotal, SampleMax, NumSamples, Scale; // h4x

    public ChartDisplay(ViewPort viewport, ChartOptions options) {
        this.viewport = viewport;
        this.options = options;
    }
}

public class Chart {
    /* Unicode Blocks
    U+2581	▁	Lower one eighth block
    U+2582	▂	Lower one quarter block
    U+2583	▃	Lower three eighths block
    U+2584	▄	Lower half block
    U+2585	▅	Lower five eighths block
    U+2586	▆	Lower three quarters block
    U+2587	▇	Lower seven eighths block
    U+2588	█	Full block

    U+2588	█	Full block
    U+2589	▉	Left seven eighths block
    U+258A	▊	Left three quarters block
    U+258B	▋	Left five eighths block
    U+258C	▌	Left half block
    U+258D	▍	Left three eighths block
    U+258E	▎	Left one quarter block
    U+258F	▏	Left one eighth block

    U+2591	░	Light shade
     */
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
                display.CurOffset = -1; // FIXME: use Nullable
                display.AvgOffset = -1;
                display.MaxOffset = -1;
                display.ScaleOffset = -1;
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
        displays.RemoveAll(display => display.viewport.buffer == buffer);
        if (displays.Count != 0) {
            throw new Exception("removeall failed");
        }
    }

    private void DrawBarToDisplay(int d, int t, int val, int max) {
        ChartDisplay display = displays[d];
        ViewPort viewport = display.viewport;
        ChartOptions options = display.options;
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

        if (display.SampleCur == -1)
            display.SampleCur = val;
        display.SampleTotal += val;
        if (val > display.SampleMax)
            display.SampleMax = val;
        display.Scale = max;
        display.NumSamples++;

        //parent.Log($"DrawBarToVP t{t}, v{val}, m{max}\nx{x}, y{y}, s{size}, dx{dx}, dy{dy}");

	double scaled = (double)val * (double)size / (double)max;
	int repeat = (int)scaled;
	int fraction = (int)((scaled * 8.0) % (double)size);

	fraction = fraction >= 4 ? 4 : 0; // Only half block is implemented in font.

	for (int i = 0; i < size; i++) {
	    if (i < repeat) {
		//buffer.Buffer[y][x] = blocks[8][0];
		viewport.Write(x, y, blocks[8]);
	    } else if (i == repeat) {
		//buffer.Buffer[y][x] = blocks[fraction][0];
		viewport.Write(x, y, blocks[fraction]);
		break;
	    }
	    x += dx;
	    y += dy;
	}
    }

    public void DrawBar(int t, int val, int max) {
        for (int d = 0, sz = displays.Count; d < sz; d++) {
            DrawBarToDisplay(d, t, val, max);
        }
    }

    public void StartDraw() {
        for (int d = 0, sz = displays.Count; d < sz; d++) {
            displays[d].SampleCur   = -1;
            displays[d].SampleTotal = 0;
            displays[d].SampleMax   = 0;
            displays[d].Scale       = 0;
            displays[d].NumSamples  = 0;
        }
    }

    public void EndDraw() {
        ChartDisplay display;
        ViewPort viewport;
        ChartOptions options;
        float avg;
        for (int d = 0, sz = displays.Count; d < sz; d++) {
            display = displays[d];
            viewport = display.viewport;
            options = display.options;
            avg = (float)display.SampleTotal / (float)display.NumSamples;
            if (options.ShowCur && display.CurOffset != -1 && display.SampleCur != -1) {
                viewport.Write(display.CurOffset, viewport.Y - 1, $"{display.SampleCur,5:G4}");
            }
            if (options.ShowAvg && display.AvgOffset != -1) {
                viewport.Write(display.AvgOffset, viewport.Y - 1, $"{avg,5:G4}");
            }
            if (options.ShowMax && display.MaxOffset != -1) {
                viewport.Write(display.MaxOffset, viewport.Y - 1, $"{display.SampleMax,5:G4}");
            }
            if (options.ShowScale && display.ScaleOffset != -1) {
                viewport.Write(display.ScaleOffset, viewport.Y - 1, $"{display.Scale,5:G4}");
            }
        }
    }
}
