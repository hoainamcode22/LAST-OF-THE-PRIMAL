# Mosasaurus automated test

Result: **PASS**

```
=== Mosasaurus === 2026-09-27 18:03:38
Avatar: valid=True human=False
Importer: 7 clips configured (3 loops, 1 events)
Materials: M_Dino_Mosasaurus
Clips in FBX: 7 (meta: 7)
Controller: Assets/Art/Characters/Dinosaurs/Mosasaurus/Animations/MosasaurusAnimator.controller (walk 2,00 m/s, run 6,00 m/s, 5 states)
LOD: 3 levels (LOD0:22680 tris, LOD1:10206 tris, LOD2:3628 tris)
Prefab: Assets/Art/Characters/Dinosaurs/Mosasaurus/Prefab/DINO_Mosasaurus.prefab
Scale: bounds size (5.63, 3.60, 12.86), min y -2,106
Collider: capsule r=1,15 h=9,26 (mesh (5.63, 3.60, 12.86))
Facing: pelvis -> head (0.03, 0.03, 1.00) (expected +Z)
Attack             1,33s loop=N events=1
Death              2,50s loop=N events=0
Hurt               0,80s loop=N events=0
Idle               3,00s loop=Y events=0 loopGap=0,0cm
Roar               2,00s loop=N events=0
Run                1,13s loop=Y events=0 loopGap=0,0cm
Walk               2,00s loop=Y events=0 loopGap=0,0cm
Sampler check: max bone travel across clips 1,60 m
Ground contact (IDLE frame 0, skinned vertices): min y -1,073 m
Skin weights: 0 of 14446 LOD0 vertices without bones
Skin check (Walk): 100,0% of LOD0 vertices move with the bones
Screenshots: Documentation/CharacterTests/Mosasaurus_clips.png (7 clips, 3/4 front view, character faces +Z)
RESULT: PASS
```
