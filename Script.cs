string _script_name = "Zephyr Industries Inventory Display";
string _script_version = "1.4.0";

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

const int PANELS_DEBUG      = 0;
const int PANELS_INV        = 1;
const int PANELS_TIME_CHART = 2;
const int SIZE_PANELS       = 3;

List<string> _panel_tags = new List<string>(SIZE_PANELS) { "@DebugDisplay", "@InventoryDisplay", "@TimeChartDisplay" };

/* Genuine global state */
List<int> _cycles = new List<int>(SIZE_CYCLES);

List<List<IMyTextPanel>> _panels = new List<List<IMyTextPanel>>(SIZE_PANELS);
List<string> _panel_text = new List<string>(SIZE_PANELS) { "", "", "" };

string _inv_text = "", _cargo_text = ""; // FIXME: StringBuilder?

List<IMyTerminalBlock> _inventory_blocks = new List<IMyTerminalBlock>();
List<IMyCargoContainer> _cargo_blocks = new List<IMyCargoContainer>();
List<IMyBatteryBlock> _battery_blocks = new List<IMyBatteryBlock>();

List<long> _load = new List<long>();
List<long> _time = new List<long>();
List<Dictionary<string, MyFixedPoint>> _item_counts = new List<Dictionary<string, MyFixedPoint>>(INV_HISTORY);

// 42x28 seems about right for 1x1 panel at 0.6
DrawBuffer _chart_buffer = new DrawBuffer(42, 28);
Chart _time_chart = new Chart(true, "Exec Time", "us");
Chart _powerstored_chart = new Chart(true, "Stored Power", "MWh");
Chart _powerin_chart = new Chart(true, "Power In", "MW");
Chart _powerout_chart = new Chart(true, "Power Out", "MW");

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

            UpdateTimeChart();
            UpdatePowerChart();
            FlushToPanels(PANELS_TIME_CHART);

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
    return $"{item_type.TypeId} {item_type.SubtypeId}";
}

public void FindPanels() {
    for (int i = 0; i < SIZE_PANELS; i++) {
        _panels[i].Clear();
        GridTerminalSystem.GetBlocksOfType<IMyTextPanel>(_panels[i], block => block.CustomName.Contains(_panel_tags[i]));
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
    //_time_chart.AddBuffer(_chart_buffer, 0, 0, 42, 12);
    //_time_chart.AddBuffer(_chart_buffer, 0, 11, 21, 6);
    //_time_chart.AddBuffer(_chart_buffer, 20, 11, 22, 6);
}

public void SetupPowerChart() {
    _powerstored_chart.AddBuffer(_chart_buffer, 0, 0, 42, 9, new ChartOptions(show_max: false, show_scale: true));
    _powerin_chart.AddBuffer(_chart_buffer, 0, 9, 42, 9, new ChartOptions(show_max: false, show_scale: true));
    _powerout_chart.AddBuffer(_chart_buffer, 0, 18, 42, 9, new ChartOptions(show_max: false, show_scale: true));
}

public void UpdateTimeChart() {
    _chart_buffer.Reset(); // FIXME: figure out where this goes

    if (_time_chart.IsViewed) {
        ClearPanels(PANELS_TIME_CHART);

        int max = (int)_time.Max();
        _time_chart.StartDraw();
        // Start at -1 because "now" hasn't been updated yet.
        for (int i = 0; i < TIME_HISTORY - 1; i++) {
            //Log($"Update T-{i,-2}");
            _time_chart.DrawBar(i, (int)_time[TimeOffset(-i - 1)], max);
        }
        _time_chart.EndDraw();

        WritePanels(PANELS_TIME_CHART, _chart_buffer.ToString());
    }
}

public void UpdatePowerChart() {
    if (_powerstored_chart.IsViewed || _powerin_chart.IsViewed || _powerout_chart.IsViewed) {
        ClearPanels(PANELS_TIME_CHART);

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
        _powerstored_chart.StartDraw();
        _powerin_chart.StartDraw();
        _powerout_chart.StartDraw();
	for (int i = 0; i < BATTERY_HISTORY; i++) {
	    //Log($"Update T-{i,-2}");
	    _powerstored_chart.DrawBar(i, (int)_battery[BatteryOffset(-i)].CurrentStoredPower, stored_max);
	    _powerin_chart.DrawBar(i, (int)_battery[BatteryOffset(-i)].CurrentInput, in_max);
	    _powerout_chart.DrawBar(i, (int)_battery[BatteryOffset(-i)].CurrentOutput, out_max);
	}
        _powerstored_chart.EndDraw();
        _powerin_chart.EndDraw();
        _powerout_chart.EndDraw();

        WritePanels(PANELS_TIME_CHART, _chart_buffer.ToString());
    }
}

public class DrawBuffer {
    public int X, Y;
    public List<StringBuilder> Buffer;
    private List<string> template;
    string blank;

    public DrawBuffer(int x, int y) {
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

    override public string ToString() {
        return string.Concat(Buffer);
    }
}

public class ViewPort {
    private DrawBuffer buffer;
    private int offsetX, offsetY;
    public int X, Y;

    public ViewPort(DrawBuffer buff, int offset_x, int offset_y, int x, int y) {
        buffer = buff;
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
    public int CurOffset = 0, AvgOffset = 0, MaxOffset = 0, ScaleOffset = 0;
    public int SampleCur, SampleTotal, SampleMax, NumSamples, Scale; // h4x

    public ChartOptions(bool horizontal = true, bool show_title = true, bool show_cur = true, bool show_avg = true, bool show_max = true, bool show_scale = false) {
        Horizontal = horizontal;
        ShowTitle = show_title;
        ShowCur = show_cur;
        ShowAvg = show_avg;
        ShowMax = show_max;
        ShowScale = show_scale;
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

    private List<ViewPort> viewports;
    private List<ChartOptions> viewport_options;
    public bool Horizontal;
    public string Title;
    public string Unit;

    public bool IsViewed { get { return viewports.Count > 0; } }

    public Chart(bool horizontal, string title, string unit) {
        viewports = new List<ViewPort>();
        viewport_options = new List<ChartOptions>();
        Title = title;
        Unit = unit;
    }

    public void AddViewPort(ViewPort viewport, ChartOptions options) {
        string label;

        viewports.Add(viewport);
        viewport_options.Add(options);

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
                options.CurOffset = offset + 4;
                offset += label.Length + 1;
            }
            if (options.ShowAvg) {
                label = $"avg:     {Unit}";
                segments.Add(label);
                options.AvgOffset = offset + 4;
                offset += label.Length + 1;
            }
            if (options.ShowMax) {
                label = $"max:     {Unit}";
                segments.Add(label);
                options.MaxOffset = offset + 4;
                offset += label.Length + 1;
            }
            if (options.ShowScale) {
                string dim = options.Horizontal ? "Y" : "X";
                label = $"{dim}:     {Unit}";
                segments.Add(label);
                options.ScaleOffset = offset + 2;
                offset += label.Length + 1;
            }
            label = "[" + string.Join(" ", segments) + "]";
            if (label.Length < viewport.X - 2) {
                offset = (viewport.X - label.Length) / 2;
                viewport.Write(offset, viewport.Y - 1, label);
                options.CurOffset += offset;
                options.AvgOffset += offset;
                options.MaxOffset += offset;
                options.ScaleOffset += offset;
            } else {
                options.CurOffset = -1;
                options.AvgOffset = -1;
                options.MaxOffset = -1;
                options.ScaleOffset = -1;
            }
        }
	viewport.Save();
    }

    public void RemoveBuffers() {
        viewports.Clear();
    }

    public void AddBuffer(DrawBuffer buffer, int offset_x, int offset_y, int x, int y, ChartOptions options) {
        AddViewPort(new ViewPort(buffer, offset_x, offset_y, x, y), options);
    }

    private void DrawBarToViewPort(int vp, int t, int val, int max) {
        ViewPort viewport = viewports[vp];
        ChartOptions options = viewport_options[vp];
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

        if (options.SampleCur == -1)
            options.SampleCur = val;
        options.SampleTotal += val;
        if (val > options.SampleMax)
            options.SampleMax = val;
        options.Scale = max;
        options.NumSamples++;

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
        for (int vp = 0, sz = viewports.Count; vp < sz; vp++) {
            DrawBarToViewPort(vp, t, val, max);
        }
    }

    public void StartDraw() {
        for (int vp = 0, sz = viewports.Count; vp < sz; vp++) {
            viewport_options[vp].SampleCur   = -1;
            viewport_options[vp].SampleTotal = 0;
            viewport_options[vp].SampleMax   = 0;
            viewport_options[vp].Scale       = 0;
            viewport_options[vp].NumSamples  = 0;
        }
    }

    public void EndDraw() {
        ViewPort viewport;
        ChartOptions options;
        float avg;
        for (int vp = 0, sz = viewports.Count; vp < sz; vp++) {
            viewport = viewports[vp];
            options = viewport_options[vp];
            avg = (float)options.SampleTotal / (float)options.NumSamples;
            if (options.ShowCur && options.CurOffset != -1 && options.SampleCur != -1) {
                viewport.Write(options.CurOffset, viewport.Y - 1, $"{options.SampleCur,5:G4}");
            }
            if (options.ShowAvg && options.AvgOffset != -1) {
                viewport.Write(options.AvgOffset, viewport.Y - 1, $"{avg,5:G4}");
            }
            if (options.ShowMax && options.MaxOffset != -1) {
                viewport.Write(options.MaxOffset, viewport.Y - 1, $"{options.SampleMax,5:G4}");
            }
            if (options.ShowScale && options.ScaleOffset != -1) {
                viewport.Write(options.ScaleOffset, viewport.Y - 1, $"{options.Scale,5:G4}");
            }
        }
    }
}
