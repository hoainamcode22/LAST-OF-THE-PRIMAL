# Island performance (editor, batch mode)

Measured inside the Unity editor on the development PC while running the PlayMode tests. Not a phone measurement.

- land creatures alive: 11, ambient creatures: 4
- frames sampled: 240
- average frame: 23,8 ms (42 fps), worst frame: 67,3 ms
- main thread (profiler): 23,6 ms
- GC allocated per frame (avg): 16,1 KB
- render counters (max): batches 3827, SetPass 93, triangles 1674261
