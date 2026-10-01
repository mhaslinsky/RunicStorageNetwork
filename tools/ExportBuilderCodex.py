"""Export the authored belt book without changing its Blender source.
Run in Blender background mode: --python this.py -- source.blend output-directory
"""
import bpy, hashlib, json, sys
from pathlib import Path
from mathutils import Vector

source, output = map(Path, sys.argv[sys.argv.index('--') + 1:])
output.mkdir(parents=True, exist_ok=True)
digest = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
source_hash = digest(source)
bpy.ops.wm.open_mainfile(filepath=str(source))
bpy.context.window.scene = bpy.data.scenes['RunicBuilderCodex_Preview']
bpy.context.view_layer.update()
deps = bpy.context.evaluated_depsgraph_get()
mapping = {
    'RBC_Leather_Charcoal': 'RBC_Leather', 'RBC_Harness_Leather': 'RBC_HarnessLeather',
    'RBC_Parchment': 'RBC_Parchment', 'RBC_Silver_TerminalMatch': 'RBC_Silver',
    'RBC_RunicBlue_Artifact': 'RBC_RunesPrimary', 'RBC_SecondaryRunes': 'RBC_RunesSecondary',
    'RBC_ClaspCrystal': 'RBC_Crystal', 'RBC_Bookmark_BlueGrey': 'RBC_Cloth'
}
groups = {}
for obj in bpy.data.collections['RunicBuilderCodex'].all_objects:
    if obj.type != 'MESH': continue
    evaluated = obj.evaluated_get(deps)
    mesh = evaluated.to_mesh(); mesh.calc_loop_triangles()
    transform = evaluated.matrix_world
    normal_transform = transform.to_3x3().inverted().transposed()
    for tri in mesh.loop_triangles:
        material = mesh.materials[tri.material_index]
        slot = mapping[material.name]
        if obj.name in ('RBC_Clasp_StrapTop', 'RBC_Clasp_ForeEdge', 'RBC_Clasp_StrapBack'):
            assert slot == 'RBC_Leather'
            slot = 'RBC_ClaspLeather'
        group = groups.setdefault(slot, dict(material=material, vertices=[], triangles=[], normals=[], smooth=[]))
        start = len(group['vertices'])
        group['vertices'].extend(list(transform @ mesh.vertices[i].co) for i in tri.vertices)
        group['triangles'].append((start, start+1, start+2))
        group['normals'].extend(list((normal_transform @ mesh.corner_normals[i].vector).normalized()) for i in tri.loops)
        group['smooth'].append(mesh.polygons[tri.polygon_index].use_smooth)
    evaluated.to_mesh_clear()
assert len(groups) == 9 and sum(len(g['triangles']) for g in groups.values()) == 1389
records = []
for slot, group in groups.items():
    material = group.pop('material')
    node = next(n for n in material.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    assert not any(n.type == 'TEX_IMAGE' for n in material.node_tree.nodes)
    records.append(dict(name=slot+'_Material', objects=[slot], base_color=list(node.inputs['Base Color'].default_value),
        metallic=node.inputs['Metallic'].default_value, roughness=node.inputs['Roughness'].default_value,
        emission_color=list(node.inputs['Emission Color'].default_value), emission_strength=node.inputs['Emission Strength'].default_value,
        backface_culling=material.use_backface_culling))
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene; scene.unit_settings.system = 'METRIC'; scene.unit_settings.scale_length = 1
root = bpy.data.objects.new('RunicBuilderCodex', None); scene.collection.objects.link(root)
for record in records:
    slot = record['objects'][0]; group = groups[slot]
    mesh = bpy.data.meshes.new(slot); mesh.from_pydata(group['vertices'], [], group['triangles']); mesh.update()
    for face, smooth in zip(mesh.polygons, group['smooth']): face.use_smooth = smooth
    mesh.normals_split_custom_set(group['normals'])
    assert max((n.vector - Vector(v)).length for n, v in zip(mesh.corner_normals, group['normals'])) < .002
    material = bpy.data.materials.new(record['name']); material.diffuse_color = record['base_color']; mesh.materials.append(material)
    obj = bpy.data.objects.new(slot, mesh); scene.collection.objects.link(obj); obj.parent = root
for obj in scene.objects: obj.select_set(True)
bpy.context.view_layer.objects.active = root
fbx = output/'RunicBuilderCodex.fbx'
bpy.ops.export_scene.fbx(filepath=str(fbx), use_selection=True, object_types={'EMPTY','MESH'}, global_scale=1,
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y', use_space_transform=True,
    bake_space_transform=False, use_mesh_modifiers=True, mesh_smooth_type='FACE', use_tspace=False,
    add_leaf_bones=False, bake_anim=False, path_mode='AUTO', embed_textures=False)
bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.fbx(filepath=str(fbx), use_custom_normals=True)
errors = {}; points = []
for obj in bpy.context.scene.objects:
    if obj.type != 'MESH': continue
    mesh = obj.data; mesh.calc_loop_triangles(); group = groups[obj.name]
    assert len(mesh.loop_triangles) == len(group['triangles'])
    normal_transform = obj.matrix_world.to_3x3().inverted().transposed()
    pe, ne = [], []
    for ti, tri in enumerate(mesh.loop_triangles):
        for j, (vi, li) in enumerate(zip(tri.vertices, tri.loops)):
            point = obj.matrix_world @ mesh.vertices[vi].co; points.append(point)
            pe.append((point - Vector(group['vertices'][ti*3+j])).length)
            ne.append(((normal_transform @ mesh.corner_normals[li].vector).normalized() - Vector(group['normals'][ti*3+j])).length)
    assert max(pe) < 1e-5 and max(ne) < .003
    errors[obj.name] = dict(position_error=max(pe), normal_error=max(ne), triangles=len(mesh.loop_triangles))
assert len(errors) == 9 and digest(source) == source_hash
low = [min(v[i] for v in points) for i in range(3)]; high = [max(v[i] for v in points) for i in range(3)]
report = dict(source='RunicBuilderCodex/model_v01/RunicBuilderCodex_v01.blend', source_sha256=source_hash,
    fbx_sha256=digest(fbx), triangles=1389, renderers=9, source_unchanged=True, bounds_blender=[low,high],
    pivot='Belt attachment at world origin in the approved Preview scene; Blender +Z up, front -Y', roundtrip=errors)
(output/'materials.json').write_text(json.dumps(dict(materials=records), indent=2), encoding='utf8')
(output/'source.json').write_text(json.dumps(report, indent=2), encoding='utf8')
print('BUILDER_EXPORT_VERIFIED', json.dumps(report), flush=True)
