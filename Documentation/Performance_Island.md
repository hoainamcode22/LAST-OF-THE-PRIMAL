# Island performance (editor, batch mode)

Measured inside the Unity editor on the development PC while running the PlayMode tests. Not a phone measurement.

- land creatures alive: 11, ambient creatures: 4
- frames sampled: 240
- average frame: 29,7 ms (34 fps), worst frame: 117,7 ms
- main thread (profiler): 29,6 ms
- GC allocated per frame (avg): 16,1 KB
- render counters (max): batches 3872, SetPass 91, triangles 1687115
