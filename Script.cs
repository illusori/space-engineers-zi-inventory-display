string _script_name = "Zephyr Industries Inventory Display";
string _script_version = "1.2.2";

string _script_title = null;
string _script_title_nl = null;

const int INV_SAMPLES  = 10;
const int LOAD_SAMPLES = 10;
const int TIME_SAMPLES = 10;

const int CYCLES_TOP  = 0;
const int CYCLES_INV  = 1;
const int SIZE_CYCLES = 2;

const int PANELS_DEBUG = 0;
const int PANELS_INV   = 1;
const int SIZE_PANELS  = 2;

List<string> _panel_tags = new List<string>(SIZE_PANELS) { "@DebugDisplay", "@InventoryDisplay" };

/* Genuine global state */
List<int> _cycles = new List<int>(SIZE_CYCLES);

List<List<IMyTextPanel>> _panels = new List<List<IMyTextPanel>>(SIZE_PANELS);
List<string> _panel_text = new List<string>(SIZE_PANELS) { "", "" };

string _inv_text = "", _cargo_text = ""; // FIXME: StringBuilder?

List<IMyTerminalBlock> _inventory_blocks = new List<IMyTerminalBlock>();
List<IMyCargoContainer> _cargo_blocks = new List<IMyCargoContainer>();

List<long> _load = new List<long>();
List<long> _time = new List<long>();
List<Dictionary<string, MyFixedPoint>> _item_counts = new List<Dictionary<string, MyFixedPoint>>(INV_SAMPLES);
MyFixedPoint _used_mass = (MyFixedPoint)0.0;
MyFixedPoint _used_volume = (MyFixedPoint)0.0;
MyFixedPoint _max_volume = (MyFixedPoint)0.0;

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
    for (int i = 0; i < LOAD_SAMPLES; i++) {
        _load.Add(0L);
    }
    for (int i = 0; i < TIME_SAMPLES; i++) {
        _time.Add(0L);
    }
    for (int i = 0; i < INV_SAMPLES; i++) {
        _item_counts.Add(new Dictionary<string, MyFixedPoint>());
    }

    FindPanels();
    FindInventoryBlocks();
    FindCargoBlocks();

    if (!Me.CustomName.Contains(_script_name)) {
        // Update our block to include our script name
        Me.CustomName = $"{Me.CustomName} - {_script_name}";
    }
    Log(_script_title);

    Runtime.UpdateFrequency |= UpdateFrequency.Update100;
}

public void Save() {
}

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
            }

            UpdateInventoryStats();
            UpdateCargoStats();

            UpdateInventoryText();
            UpdateCargoText();
            CompositeInventoryPanel();

            FlushToPanels(PANELS_INV);

	    _load[_cycles[CYCLES_TOP] % LOAD_SAMPLES] = Runtime.CurrentInstructionCount;
	    _time[_cycles[CYCLES_TOP] % TIME_SAMPLES] = (DateTime.Now - start_time).Ticks;

	    long load_avg = _load.Sum() / LOAD_SAMPLES;
	    long time_avg = (_time.Sum() * 1000L) / (TIME_SAMPLES * TimeSpan.TicksPerMillisecond);
	    Log($"Load avg {load_avg}/{Runtime.MaxInstructionCount} in {time_avg}us");

            for (int i = 0; i < 5; i++) {
                long load = _load[(LOAD_SAMPLES + _cycles[CYCLES_TOP] - i) % LOAD_SAMPLES];
                long time = (_time[(TIME_SAMPLES + _cycles[CYCLES_TOP] - i) % TIME_SAMPLES] * 1000L) / TimeSpan.TicksPerMillisecond;
                Log($"  [T-{i}] Load {load} in {time}us");
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
    int last = (_cycles[CYCLES_INV] + 1) % INV_SAMPLES,
        current = _cycles[CYCLES_INV] % INV_SAMPLES;
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
    _used_mass = (MyFixedPoint)0.0;
    _used_volume = (MyFixedPoint)0.0;
    _max_volume = (MyFixedPoint)0.0;
    num_invs = 0;
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
            _used_mass   = MyFixedPoint.AddSafe(_used_mass, inv.CurrentMass);
            _used_volume = MyFixedPoint.AddSafe(_used_volume, inv.CurrentVolume);
            _max_volume  = MyFixedPoint.AddSafe(_max_volume, inv.MaxVolume);
        }
    }
    Log($"  {num_invs} inventories in {_cargo_blocks.Count} cargoes.");
}

void UpdateInventoryText() {
    MyFixedPoint old, value;
    int delta;
    _inv_text = "";
    foreach (KeyValuePair<string, MyFixedPoint> kvp in _item_counts[current]) {
        value = kvp.Value;
        old = (MyFixedPoint)0.0;
        _item_counts[last].TryGetValue(kvp.Key, out old);
        delta = (int)MyFixedPoint.AddSafe(value, old == null ? -value : -old) / INV_SAMPLES;
        if (delta != 0) {
            _inv_text += $"{(int)value,8} {kvp.Key} [{delta,0:+#;-#;0}]\n";
        } else {
            _inv_text += $"{(int)value,8} {kvp.Key}\n";
        }
    }
}

void UpdateCargoText() {
    MyFixedPoint free_volume = MyFixedPoint.AddSafe(_max_volume, -_used_volume);
    _cargo_text = $"{(int)_used_mass}kg {(int)_used_volume}/{(int)_max_volume}m3 {(int)free_volume}m3 free.\n";
}

void CompositeInventoryPanel() {
    ClearPanels(PANELS_INV);
    WritePanels(PANELS_INV, $"{_script_title_nl}\n{_cargo_text}\n{_inv_text}");
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
