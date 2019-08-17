# space-engineers-zi-inventory-display
Space Engineers - Zephyr Industries Inventory Display

## Warning:

Zephyr Industries Inventory Display is functional but not particularly user-friendly at this stage. There isn't a great deal of documentation without looking at the source code for the script. If you're interested in learning how to write your own scripts, it's probably got some intermediate-level ideas in there for you to be inspired by.

## Living in the Future(tm)

*Ever wanted to see your entire bases's inventory at a glance? To see pretty charts of how your power demand has fluctuated over time?*

Zephyr Industries has what you need, I'm here to tell you about their great new product: _Zephyr Industries Inventory Display_.

Display sorted lists of your base inventory contents! Display bar charts of power supply, demand, and storage or of cargo capacity, usage, and free space. Display multiple charts on one LCD panel!

Life has never been so good, that's what Living in the Future(tm) means!

## Instructions:
* Place on a Programmable Block.
* The script will run automatically every 100 game updates, and scan for updates to your base every 300 game updates (~30-60 seconds).
* Mark LCD panels by adding a tag to their name and on the next base scan the script will start using it.
  * `@InventoryDisplay` is a sorted list of items and quantities and some basic stats.
  * `@DebugDisplay` displays info useful for script development, including performance.
  * `@WarningDisplay` displays any issues the script encountered.
  * `@ChartDisplay` will configure the display for charts. Configuration is a bit more complicated, see the *Chart Displays* section for more details.

## Notes:
* Yeah, Isy's Inventory Manager does most of this and far more. I suggest you go use that unless one of the features in this script really appeals to you. I wrote this as a learning exercise for myself (my first C# project) and to suit my personal needs rather than to be a replacement for a script that has been actively developed and supported for years.

## Chart Displays:

To configure a chart display you need tag the name with `@ChartDisplay` and to edit the Custom Data for the display.

The Custom Data follows an INI-file format with section names indicating what chart you'd like to display and section keys adding extra parameters to the chart.

Some examples are probably a bit easier to understand.

### Basic Power Stored Chart

Set the Custom Data to:

```
[power_stored]
```

This creates one chart tracking the `power_stored` series with the default options: fill the entire panel, have the bars aligned vertically and time horizontal.

### Triple Power Chart

```
[power_stored]
height=13

[power_in]
y=13
height=11

[power_out]
y=24
height=11
```

This places three charts onto one display folowing the `power_stored`, `power_in` and `power_out` series. It also overrides the default layout so that they tile one above the other taking up about a third of the height of the panel each and the full width.

### List of chart series

Series name | Description
--- | ---
power_stored | How much power is stored in your batteries.
power_in | How much power is entering your batteries.
power_out | How much power is leaving your batteries.
cargo_used_mass | How much mass (tonnes) of cargo is within all cargo containers.
cargo_used_volume | How much volume (m3) is used within all cargo containers.
cargo_free_volume | How much volume (m3) is free within all cargo containers.
time | (debug) Microsecond timings of how long the script ran for on each invocation.

### List of chart options

Option | Default | Description
:---: | :---: | :---
x | 0 | Panel column to start the chart at. 0 is leftmost column.
y | 0 | Panel line to start the chart at. 0 is topmost line.
width | panel width | Number of panel columns to span. 52 is max for 1x1 panel, 104 for 2x1.
height | panel height | Number of panel lines to span. 35 is max for both 1x1 and 2x1 panels.
name | no value | If set it will be used for the chart series instead of the section name.
horizontal | true | If false, the chart will run top to bottom rather than right to left.
show_title | true | Should the chart title be displayed in the top border?
show_cur | true | Should the current series value be displayed in the bottom border?
show_avg | true | Should the average value of the displayed bars be shown?
show_max | false | Should the max value of the displayed bars be shown?
show_scale | true | Should the scale (max Y point) be displayed in the bottom border?

The scale is automatically set by some heuristics that sorta make sense and seem to work for me.

There's currently no way to set the title to be something other than the name of the series.

Setting x/y/width/height values that are outside the bounds of the display will stop the script, you'll need to fix the values then recompile the script. As I said at the top, it isn't very user-friendly right now.

## Contributing:

Zephyr Industries Inventory Display is open source, under an MIT license. You can contribute to or copy the code at https://github.com/illusori/space-engineers-zi-inventory-display.
