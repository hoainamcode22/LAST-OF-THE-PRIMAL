# Video findings (Lead, owner's recording 2026-09-28 19:19, 1916x828, 30 fps, ~27 s of gameplay)

Frame sheets (10 fps crops around the character): , ,
, , ; 2 fps overview .
Owner's words: "nhân vật khá khựng, cánh tay thì xoay xoay" (character is jerky / stiff, the arms keep twisting).
No weapon in hand during the whole clip (unarmed locomotion + gathering).

| # | Time (s) | What is seen | Suspected layer |
|---|---|---|---|
| V1 | 0-8, 17-27 (run) | Hands carried at chin / shoulder height the whole cycle; upper arms abducted, elbows flared ~40-60 deg away from the torso; the forearm pumps up and down near the face instead of a fore-aft swing from the shoulder. Torso looks rigid, little shoulder counter-rotation seen from behind. Reads as "boxer / gorilla arms". | clip content (Run) and/or IK elbow hints / retarget |
| V2 | 0-26 (run) | Fist / wrist visibly rolls during the cycle ("arms twisting"). | wrist roll keys, TwistBoneDriver, humanoid twist settings |
| V3 | 2.7-4.5 (turn while walking) | Body yaw turns quickly with no pivot step; feet slide during the turn; arms jerk. | motor turn rate / no turn clips |
| V4 | 9.1-9.2 (gather start) | Standing to deep squat in <= 0.1 s (snap, no visible transition). | animator transition / action crossfade |
| V5 | 10.3-10.4 (gather loop) | Whole body yaw snaps ~45 deg toward the driftwood in one frame. | PlayerInteraction focus rotation |
| V6 | 9.3-15.5 (gather loop) | Right arm rises with the fist rotating; loop reads mechanical. | GatherPlant clip |
| V7 | 15.8-16.3 (gather end) | Crouch to stand pops; one frame (~16.2) has legs spread and both arms flung out sideways (blend passing through an abducted pose), then idle. | transition out of the action / blend path |
| V8 | 16.3-17.0 (walk) | Arms hang almost straight with little swing; short stiff steps. | Walk clip |
| V9 | 18.0-18.5 | Camera passes through a tree trunk (dither fade). | camera collision (not character) |
