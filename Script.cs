string _script_name = "Zephyr Industries Inventory Display";
string _script_version = "3.0.0";

string _script_title = null;
string _script_title_nl = null;

const string PUBSUB_SCRIPT_NAME = "Zephyr Industries PubSub Controller";
const string PUBSUB_ID = "zi.inv-display";

const string BARCHARTS_SCRIPT_NAME = "Zephyr Industries Bar Charts";

const int INV_HISTORY     = 100;
const int LOAD_HISTORY    = 100;
const int CARGO_HISTORY   = 100;
const int BATTERY_HISTORY = 100;
const int GAS_HISTORY     = 100;
const int PROD_HISTORY    = 100;

const int INV_SAMPLES     = 10;
const int LOAD_SAMPLES    = 10;
const int CARGO_SAMPLES   = 10;
const int BATTERY_SAMPLES = 10;
const int GAS_SAMPLES     = 10;
const int PROD_SAMPLES    = 10;

const int CYCLES_TOP     = 0;
const int CYCLES_INV     = 1;
const int CYCLES_CARGO   = 2;
const int CYCLES_BATTERY = 3;
const int CYCLES_GAS     = 4;
const int CYCLES_PROD    = 5;
const int SIZE_CYCLES    = 6;

const int PANELS_DEBUG = 0;
const int PANELS_WARN  = 1;
const int PANELS_INV   = 2;
const int SIZE_PANELS  = 3;

const string CHART_TIME                = "Inv Exec Time";
const string CHART_LOAD                = "Inv Instr Load";
const string CHART_POWER_STORED        = "Stored Power";
const string CHART_MAX_POWER_STORED    = "Max Stored Power";
const string CHART_POWER_IN            = "Power In";
const string CHART_POWER_OUT           = "Power Out";
const string CHART_CARGO_USED_MASS     = "Cargo Mass";
const string CHART_CARGO_USED_VOLUME   = "Cargo Vol";
const string CHART_CARGO_FREE_VOLUME   = "Cargo Free";
const string CHART_O2_USED_VOLUME      = "O2 Vol";
const string CHART_O2_FREE_VOLUME      = "O2 Free";
const string CHART_H2_USED_VOLUME      = "H2 Vol";
const string CHART_H2_FREE_VOLUME      = "H2 Free";
const string CHART_ALL_ASSEM_ACTIVE    = "All Active Assemblers";
const string CHART_ALL_ASSEM_TOTAL     = "All Assemblers";
const string CHART_ASSEM_ACTIVE        = "Active Assemblers";
const string CHART_ASSEM_TOTAL         = "Assemblers";
const string CHART_BASIC_ASSEM_ACTIVE  = "Active Basic Assemblers";
const string CHART_BASIC_ASSEM_TOTAL   = "Basic Assemblers";
const string CHART_ALL_REFINE_ACTIVE   = "All Active Refineries";
const string CHART_ALL_REFINE_TOTAL    = "All Refineries";
const string CHART_REFINE_ACTIVE       = "Active Refineries";
const string CHART_REFINE_TOTAL        = "Refineries";
const string CHART_BASIC_REFINE_ACTIVE = "Active Basic Refineries";
const string CHART_BASIC_REFINE_TOTAL  = "Basic Refineries";
const string CHART_SURV_KIT_ACTIVE     = "Active Survival Kits";
const string CHART_SURV_KIT_TOTAL      = "Survival Kits";

MyIni _ini = new MyIni();

List<string> _panel_tags = new List<string>(SIZE_PANELS) { "@DebugDisplay", "@WarningDisplay", "@InventoryDisplay" };

/* Genuine global state */
List<int> _cycles = new List<int>(SIZE_CYCLES);

List<List<IMyTextPanel>> _panels = new List<List<IMyTextPanel>>(SIZE_PANELS);
List<string> _panel_text = new List<string>(SIZE_PANELS) { "", "", "", "", "" };
List<IMyProgrammableBlock> _pubsub_blocks = new List<IMyProgrammableBlock>();

string _inv_text = "", _cargo_text = ""; // FIXME: StringBuilder?

List<IMyTerminalBlock> _inventory_blocks = new List<IMyTerminalBlock>();
List<IMyProgrammableBlock> _chart_blocks = new List<IMyProgrammableBlock>();
List<IMyCargoContainer> _cargo_blocks = new List<IMyCargoContainer>();
List<IMyBatteryBlock> _battery_blocks = new List<IMyBatteryBlock>();
List<IMyGasTank> _gas_tank_blocks = new List<IMyGasTank>();
List<IMyProductionBlock> _prod_blocks = new List<IMyProductionBlock>();

class LoadSample {
    public double Load;
    public double Time;
}

class CargoSample {
    public MyFixedPoint UsedMass, UsedVolume, MaxVolume;
}

class BatterySample {
    public float CurrentStoredPower, MaxStoredPower, CurrentInput, MaxInput, CurrentOutput, MaxOutput;
}

class GasSample {
    public double CurrentStoredO2, MaxStoredO2, CurrentStoredH2, MaxStoredH2;
}

class ProdSample {
    public int AllAssemActive, AssemActive, BasicAssemActive;
    public int AllAssemTotal, AssemTotal, BasicAssemTotal;
    public int AllRefineActive, RefineActive, BasicRefineActive;
    public int AllRefineTotal, RefineTotal, BasicRefineTotal;
    public int SurvKitActive;
    public int SurvKitTotal;
}

List<LoadSample> _load = new List<LoadSample>(LOAD_HISTORY);
List<Dictionary<MyItemType, double>> _item_counts = new List<Dictionary<MyItemType, double>>(INV_HISTORY);
List<CargoSample> _cargo = new List<CargoSample>(CARGO_HISTORY);
List<BatterySample> _battery = new List<BatterySample>(BATTERY_HISTORY);
List<GasSample> _gas = new List<GasSample>(GAS_HISTORY);
List<ProdSample> _prod = new List<ProdSample>(PROD_HISTORY);


double load_sample_total = 0.0;
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
    for (int i = 0; i < GAS_HISTORY; i++) {
        _gas.Add(new GasSample());
    }
    for (int i = 0; i < PROD_HISTORY; i++) {
        _prod.Add(new ProdSample());
    }

    FindPanels();
    FindPubSubBlocks();
    FindChartBlocks();
    FindInventoryBlocks();
    FindCargoBlocks();
    FindBatteryBlocks();
    FindGasBlocks();
    FindProdBlocks();

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
public int GasOffset(int delta) { return SafeMod(_cycles[CYCLES_GAS] + delta, GAS_HISTORY); }
public int ProdOffset(int delta) { return SafeMod(_cycles[CYCLES_PROD] + delta, PROD_HISTORY); }

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
                FindPubSubBlocks();
            }
            if ((_cycles[CYCLES_TOP] % 30) == 2) {
                FindChartBlocks();
            }
            if ((_cycles[CYCLES_TOP] % 30) == 3) {
                FindInventoryBlocks();
            }
            if ((_cycles[CYCLES_TOP] % 30) == 4) {
                FindCargoBlocks();
            }
            if ((_cycles[CYCLES_TOP] % 30) == 5) {
                FindBatteryBlocks();
            }
            if ((_cycles[CYCLES_TOP] % 30) == 6) {
                FindGasBlocks();
            }
            if ((_cycles[CYCLES_TOP] % 30) == 7) {
                FindProdBlocks();
            }

            UpdateInventoryStats();
            UpdateCargoStats();
            UpdateBatteryStats();
            UpdateGasStats();
            UpdateProdStats();

            UpdateInventoryText();
            UpdateCargoText();
            CompositeInventoryPanel();

            Log($"  {_pubsub_blocks.Count()} pubsub and {_chart_blocks.Count()} chart blocks found.");

            FlushToPanels(PANELS_INV);

	    UpdateTimeChart();
	    UpdatePowerCharts();
	    UpdateCargoCharts();
	    UpdateGasCharts();
	    UpdateProdCharts();

	    _load[LoadOffset(0)].Load = (double)Runtime.CurrentInstructionCount * 100.0 / (double)Runtime.MaxInstructionCount;
	    //_load[LoadOffset(0)].Time = (DateTime.Now - start_time).Ticks;

            load_sample_total = load_sample_total - _load[LoadOffset(-LOAD_SAMPLES - 1)].Load + _load[LoadOffset(0)].Load;
            time_sample_total = time_sample_total - _load[LoadOffset(-LOAD_SAMPLES - 2)].Time + _load[LoadOffset(-1)].Time;
	    long load_avg = (long)(load_sample_total / (double)LOAD_SAMPLES);
	    long time_avg = TimeAsUsec(time_sample_total) / (long)LOAD_SAMPLES;
	    Log($"Load avg {load_avg}% in {time_avg}us");

            // Start at T-1 - exec time hasn't been updated yet.
            for (int i = 1; i < 16; i++) {
                long load = (long)_load[LoadOffset(-i)].Load;
                long time = TimeAsUsec(_load[LoadOffset(-i)].Time);
                Log($"  [T-{i,-2}] Load {load}% in {time}us");
            }
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

public void FindPubSubBlocks() {
    _pubsub_blocks.Clear();
    GridTerminalSystem.GetBlocksOfType<IMyProgrammableBlock>(_pubsub_blocks, block => block.CustomName.Contains(PUBSUB_SCRIPT_NAME) && block.IsSameConstructAs(Me));
    // If we registered any events, we'd do it like this:
    //IssueEvent("pubsub.register", $"datapoint.issue {Me.EntityId}");
}

public void IssueEvent(string event_name, string event_args) {
    foreach (IMyProgrammableBlock block in _pubsub_blocks) {
        if (block != null) {
            block.TryRun($"event {PUBSUB_ID} {event_name} {event_args}");
        }
    }
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

public void UpdateGasStats() {
    _cycles[CYCLES_GAS]++;

    int last = GasOffset(-GAS_SAMPLES), current = GasOffset(0);
    GasSample sample = _gas[current];

    sample.CurrentStoredO2 = 0.0F;
    sample.MaxStoredO2     = 0.0F;
    sample.CurrentStoredH2 = 0.0F;
    sample.MaxStoredH2     = 0.0F;

    // TODO: stockpile counts?
    int num_o2_tanks = 0, num_h2_tanks = 0;
    foreach (IMyGasTank gas_tank_block in _gas_tank_blocks) {
        if (gas_tank_block == null) {
            //Log("Block is null.");
            continue;
        }
        // Such a hack :/
        if (gas_tank_block.DefinitionDisplayNameText == "Oxygen Tank") {
            num_o2_tanks++;
            sample.CurrentStoredO2 += (float)(gas_tank_block.Capacity * gas_tank_block.FilledRatio);
            sample.MaxStoredO2 += (float)gas_tank_block.Capacity;
        } else if (gas_tank_block.DefinitionDisplayNameText == "Hydrogen Tank") {
            num_h2_tanks++;
            sample.CurrentStoredH2 += (float)(gas_tank_block.Capacity * gas_tank_block.FilledRatio);
            sample.MaxStoredH2 += (float)gas_tank_block.Capacity;
        }
    }
    Log($"  {num_o2_tanks} O2 tanks and {num_h2_tanks} H2 tanks.");
}

public void UpdateProdStats() {
    _cycles[CYCLES_PROD]++;

    int last = ProdOffset(-PROD_SAMPLES), current = ProdOffset(0);
    ProdSample sample = _prod[current];

    sample.AllAssemActive    = 0;
    sample.AssemActive       = 0;
    sample.BasicAssemActive  = 0;
    sample.AllAssemTotal     = 0;
    sample.AssemTotal        = 0;
    sample.BasicAssemTotal   = 0;
    sample.AllRefineActive   = 0;
    sample.RefineActive      = 0;
    sample.BasicRefineActive = 0;
    sample.AllRefineTotal    = 0;
    sample.RefineTotal       = 0;
    sample.BasicRefineTotal  = 0;
    sample.SurvKitActive     = 0;
    sample.SurvKitTotal      = 0;

    foreach (IMyProductionBlock prod_block in _prod_blocks) {
        if (prod_block == null) {
            //Log("Block is null.");
            continue;
        }
        if (prod_block is IMyAssembler) {
            // Such a hack :/
            if (prod_block.DefinitionDisplayNameText == "Assembler") {
                sample.AllAssemTotal++;
                sample.AssemTotal++;
                if (prod_block.IsProducing) {
                    sample.AllAssemActive++;
                    sample.AssemActive++;
                }
            } else if (prod_block.DefinitionDisplayNameText == "Basic Assembler") {
                sample.AllAssemTotal++;
                sample.BasicAssemTotal++;
                if (prod_block.IsProducing) {
                    sample.AllAssemActive++;
                    sample.BasicAssemActive++;
                }
            } else if (prod_block.DefinitionDisplayNameText == "Survival kit") {
                sample.SurvKitTotal++;
                if (prod_block.IsProducing) {
                    sample.SurvKitActive++;
                }
            } else {
                Log($"Unknown assembler block type: {prod_block.DefinitionDisplayNameText}.");
            }
        } else if (prod_block is IMyRefinery) {
            // Such a hack :/
            if (prod_block.DefinitionDisplayNameText == "Refinery") {
                sample.AllRefineTotal++;
                sample.RefineTotal++;
                if (prod_block.IsProducing) {
                    sample.AllRefineActive++;
                    sample.RefineActive++;
                }
            } else if (prod_block.DefinitionDisplayNameText == "Basic Refinery") {
                sample.AllRefineTotal++;
                sample.BasicRefineTotal++;
                if (prod_block.IsProducing) {
                    sample.AllRefineActive++;
                    sample.BasicRefineActive++;
                }
            } else {
                Log($"Unknown refinery block type: {prod_block.DefinitionDisplayNameText}.");
            }
        } else {
            Log($"Unknown production block type: {prod_block.DefinitionDisplayNameText}.");
        }
    }
    Log($"  {sample.AllAssemActive}/{sample.AllAssemTotal} assemblers and {sample.AllRefineActive}/{sample.AllRefineTotal} refineries active.");
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
    if (item_type.TypeId == "MyObjectBuilder_ConsumableItem") {
        return item_type.SubtypeId;
    }
    if (item_type.TypeId == "MyObjectBuilder_PhysicalObject") {
        return item_type.SubtypeId;
    }
    return $"{item_type.TypeId} {item_type.SubtypeId}";
}

public void FindPanels() {
    for (int i = 0; i < SIZE_PANELS; i++) {
        _panels[i].Clear();
        GridTerminalSystem.GetBlocksOfType<IMyTextPanel>(_panels[i], block => block.CustomName.Contains(_panel_tags[i]) && block.IsSameConstructAs(Me));
        for (int j = 0, szj = _panels[i].Count; j < szj; j++) {
            _panels[i][j].ContentType = ContentType.TEXT_AND_IMAGE;
            _panels[i][j].Font = "Monospace";
            _panels[i][j].FontSize = 0.5F;
            _panels[i][j].TextPadding = 0.5F;
            _panels[i][j].Alignment = TextAlignment.LEFT;
        }
    }
}

// Used as a fallback if they don't have PubSub installed.
public void SendChartCommand(string command) {
    for (int i = 0, sz = _chart_blocks.Count; i < sz; i++) {
        _chart_blocks[i].TryRun(command);
    }
}

public void UpdateChart(string chart, double value) {
    if (_pubsub_blocks.Count != 0) {
        IssueEvent("datapoint.issue", $"\"{chart}\" {value}");
    } else {
        SendChartCommand($"add \"{chart}\" {value}");
    }
}

public void CreateChart(string chart, string unit) {
    if (_pubsub_blocks.Count != 0) {
        IssueEvent("dataset.create", $"\"{chart}\" \"{unit}\"");
    } else {
        SendChartCommand($"create \"{chart}\" \"{unit}\"");
    }
}

public void FindChartBlocks() {
    _chart_blocks.Clear();
    GridTerminalSystem.GetBlocksOfType<IMyProgrammableBlock>(_chart_blocks, block => block.CustomName.Contains(BARCHARTS_SCRIPT_NAME) && block.IsSameConstructAs(Me));
    CreateChart(CHART_TIME, "us");
    CreateChart(CHART_LOAD, "%");
    CreateChart(CHART_POWER_STORED, "MWh");
    CreateChart(CHART_MAX_POWER_STORED, "MWh");
    CreateChart(CHART_POWER_IN, "MW");
    CreateChart(CHART_POWER_OUT, "MW");
    CreateChart(CHART_CARGO_USED_MASS, "kt");
    CreateChart(CHART_CARGO_USED_VOLUME, "m3");
    CreateChart(CHART_CARGO_FREE_VOLUME, "m3");
    CreateChart(CHART_O2_USED_VOLUME, "m3");
    CreateChart(CHART_O2_FREE_VOLUME, "m3");
    CreateChart(CHART_H2_USED_VOLUME, "m3");
    CreateChart(CHART_H2_FREE_VOLUME, "m3");
    CreateChart(CHART_ALL_ASSEM_ACTIVE, "");
    CreateChart(CHART_ALL_ASSEM_TOTAL, "");
    CreateChart(CHART_ASSEM_ACTIVE, "");
    CreateChart(CHART_ASSEM_TOTAL, "");
    CreateChart(CHART_BASIC_ASSEM_ACTIVE, "");
    CreateChart(CHART_BASIC_ASSEM_TOTAL, "");
    CreateChart(CHART_ALL_REFINE_ACTIVE, "");
    CreateChart(CHART_ALL_REFINE_TOTAL, "");
    CreateChart(CHART_REFINE_ACTIVE, "");
    CreateChart(CHART_REFINE_TOTAL, "");
    CreateChart(CHART_BASIC_REFINE_ACTIVE, "");
    CreateChart(CHART_BASIC_REFINE_TOTAL, "");
    CreateChart(CHART_SURV_KIT_ACTIVE, "");
    CreateChart(CHART_SURV_KIT_TOTAL, "");
}

public void FindInventoryBlocks() {
    _inventory_blocks.Clear();
    GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(_inventory_blocks, block => block.HasInventory && block.IsSameConstructAs(Me));
    //Log($"Found {_inventory_blocks.Count} inventory blocks.");
}

public void FindCargoBlocks() {
    _cargo_blocks.Clear();
    GridTerminalSystem.GetBlocksOfType<IMyCargoContainer>(_cargo_blocks, block => block.HasInventory && block.IsSameConstructAs(Me));
    //Log($"Found {_cargo_blocks.Count} cargo blocks.");
}

public void FindBatteryBlocks() {
    _battery_blocks.Clear();
    GridTerminalSystem.GetBlocksOfType<IMyBatteryBlock>(_battery_blocks, block => block.IsSameConstructAs(Me));
    //Log($"Found {_battery_blocks.Count} battery blocks.");
}

public void FindGasBlocks() {
    _gas_tank_blocks.Clear();
    GridTerminalSystem.GetBlocksOfType<IMyGasTank>(_gas_tank_blocks, block => block.IsSameConstructAs(Me));
    //Log($"Found {_gas_tank_blocks.Count} gas tank blocks.");
}

public void FindProdBlocks() {
    _prod_blocks.Clear();
    GridTerminalSystem.GetBlocksOfType<IMyProductionBlock>(_prod_blocks, block => block.IsSameConstructAs(Me));
    //Log($"Found {_prod_blocks.Count} production blocks.");
}

/*
public void FindPowerProducerBlocks() {
    _power_blocks.Clear();
    GridTerminalSystem.GetBlocksOfType<IMyPowerProducer>(_power_blocks, block => block.IsSameConstructAs(Me));
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

public void PrependPanels(int kind, string s) {
    _panel_text[kind] = s + _panel_text[kind];
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
    // Never clear buffer and and always immediately flush.
    // Prepend because long text will have the bottom hidden.
    PrependPanels(PANELS_WARN, $"[{DateTime.Now,11:HH:mm:ss.ff}] {s}\n");
    FlushToPanels(PANELS_WARN);
}

public void UpdateTimeChart() {
    UpdateChart(CHART_TIME, (double)TimeAsUsec(_load[LoadOffset(-1)].Time));
    UpdateChart(CHART_LOAD, _load[LoadOffset(-1)].Load);
}

public void UpdatePowerCharts() {
    int now = BatteryOffset(0);
    UpdateChart(CHART_POWER_STORED, (double)_battery[now].CurrentStoredPower);
    UpdateChart(CHART_MAX_POWER_STORED, (double)_battery[now].MaxStoredPower);
    UpdateChart(CHART_POWER_IN, (double)_battery[now].CurrentInput);
    UpdateChart(CHART_POWER_OUT, (double)_battery[now].CurrentOutput);
}

public void UpdateCargoCharts() {
    int now = CargoOffset(0);
    UpdateChart(CHART_CARGO_USED_MASS, (double)_cargo[now].UsedMass / 1000000.0);
    UpdateChart(CHART_CARGO_USED_VOLUME, (double)_cargo[now].UsedVolume);
    UpdateChart(CHART_CARGO_FREE_VOLUME, (double)_cargo[now].MaxVolume - (double)_cargo[now].UsedVolume);
}

public void UpdateGasCharts() {
    int now = GasOffset(0);
    // Recorded as Litres displaying as m3: 1000L = 1m3.
    UpdateChart(CHART_O2_USED_VOLUME, (double)_gas[now].CurrentStoredO2 / 1000.0);
    UpdateChart(CHART_O2_FREE_VOLUME, ((double)_gas[now].MaxStoredO2 - (double)_gas[now].CurrentStoredO2) / 1000.0);
    UpdateChart(CHART_H2_USED_VOLUME, (double)_gas[now].CurrentStoredH2 / 1000.0);
    UpdateChart(CHART_H2_FREE_VOLUME, ((double)_gas[now].MaxStoredH2 - (double)_gas[now].CurrentStoredH2) / 1000.0);
}

public void UpdateProdCharts() {
    int now = ProdOffset(0);
    ProdSample sample = _prod[now];
    UpdateChart(CHART_ALL_ASSEM_ACTIVE, (double)sample.AllAssemActive);
    UpdateChart(CHART_ALL_ASSEM_TOTAL, (double)sample.AllAssemTotal);
    UpdateChart(CHART_ASSEM_ACTIVE, (double)sample.AssemActive);
    UpdateChart(CHART_ASSEM_TOTAL, (double)sample.AssemTotal);
    UpdateChart(CHART_BASIC_ASSEM_ACTIVE, (double)sample.BasicAssemActive);
    UpdateChart(CHART_BASIC_ASSEM_TOTAL, (double)sample.BasicAssemTotal);
    UpdateChart(CHART_ALL_REFINE_ACTIVE, (double)sample.AllRefineActive);
    UpdateChart(CHART_ALL_REFINE_TOTAL, (double)sample.AllRefineTotal);
    UpdateChart(CHART_REFINE_ACTIVE, (double)sample.RefineActive);
    UpdateChart(CHART_REFINE_TOTAL, (double)sample.RefineTotal);
    UpdateChart(CHART_BASIC_REFINE_ACTIVE, (double)sample.BasicRefineActive);
    UpdateChart(CHART_BASIC_REFINE_TOTAL, (double)sample.BasicRefineTotal);
    UpdateChart(CHART_SURV_KIT_ACTIVE, (double)sample.SurvKitActive);
    UpdateChart(CHART_SURV_KIT_TOTAL, (double)sample.SurvKitTotal);
}
