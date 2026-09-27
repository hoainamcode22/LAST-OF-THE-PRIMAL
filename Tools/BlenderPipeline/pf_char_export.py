# Export a rigged character (armature + LOD meshes + all actions) and its textures/meta to the Unity project.
import bpy, os, json, shutil, re
import pf_ops

UNITY = r"E:\LAST OF THE PRIMAL"
TEX = r"E:\Model game khủng long\textures"

def export(arm, meshes, dest_dir, fbx_name, actions, tex_prefixes, meta, meta_name, use_mesh_modifiers=True, axis_forward='-Z', face_unity_forward=False):
    model_dir = os.path.join(dest_dir, "Model"); tex_dir = os.path.join(dest_dir, "Textures"); anim_dir = os.path.join(dest_dir, "Animations")
    for d in (model_dir, tex_dir, anim_dir, os.path.join(dest_dir, "Materials"), os.path.join(dest_dir, "Prefab")):
        os.makedirs(d, exist_ok=True)
    arm.animation_data_create()
    arm.animation_data.action = None
    arm.data.pose_position = 'POSE'
    for pb in arm.pose.bones:
        pb.rotation_quaternion = (1, 0, 0, 0); pb.location = (0, 0, 0); pb.scale = (1, 1, 1)
    # only the character's actions are exported
    keep = set(actions)
    fake = {}
    for a in bpy.data.actions:
        fake[a.name] = a.use_fake_user
    removed = []
    path = os.path.join(model_dir, fbx_name + ".fbx")
    # temporarily hide unrelated actions by renaming them with a prefix the exporter still sees -> instead mute via users
    objs = [arm] + list(meshes)
    # Blender characters face -Y; Unity characters must face +Z -> turn the rig object 180 deg for the export only
    rot0 = arm.rotation_euler.copy()
    if face_unity_forward:
        import math
        arm.rotation_euler = (rot0.x, rot0.y, rot0.z + math.pi); bpy.context.view_layer.update()
    pf_ops.run(bpy.ops.export_scene.fbx, objs, active=arm, filepath=path, use_selection=True,
               object_types={'ARMATURE', 'MESH'}, axis_forward=axis_forward, axis_up='Y', apply_unit_scale=True,
               apply_scale_options='FBX_SCALE_ALL', bake_space_transform=False, add_leaf_bones=False,
               primary_bone_axis='Y', secondary_bone_axis='X', armature_nodetype='NULL', use_armature_deform_only=False,
               bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
               bake_anim_force_startend_keying=True, bake_anim_step=1.0, bake_anim_simplify_factor=0.0,
               mesh_smooth_type='FACE', use_tspace=True, use_mesh_modifiers=use_mesh_modifiers, path_mode='STRIP')
    arm.rotation_euler = rot0; bpy.context.view_layer.update()
    txt = open(path, 'rb').read()
    takes = sorted(set(m.decode(errors='ignore') for m in re.findall(rb"Take\x00*S.\x00\x00\x00([A-Za-z0-9_\|\.]+)", txt)))
    copied = []
    for f in os.listdir(TEX):
        if any(f.startswith(p) for p in tex_prefixes) and f.endswith(".png") and "_AO" not in f:
            shutil.copy2(os.path.join(TEX, f), os.path.join(tex_dir, f)); copied.append(f)
    json.dump(meta, open(os.path.join(anim_dir, meta_name), "w"), indent=1)
    return dict(fbx=path, bytes=os.path.getsize(path), textures=copied, takes_found=len(takes))
