# Terminal picture sizing

`max_picture_columns` (default **200**) and `max_picture_rows` (default **100**) in the Cosmetic configuration limit the cell area of figures sent to telnet and MXP clients. Both settings are available through the configuration editor and propagate to the rendering worker with the other output settings.

Pictures retain their aspect ratio and fit the requested layout and client width. These limits do not enlarge small source images and do not change browser portal rendering. Columns may be set from 1 to 2000; rows from 1 to 1000.
