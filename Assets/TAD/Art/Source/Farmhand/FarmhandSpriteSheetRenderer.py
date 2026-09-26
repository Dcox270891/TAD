"""Render TAD's modular low-poly Farmhand sprites with Blender.

This procedural Blender source lives beside the editable .blend file. It produces
the transparent sprite sheets consumed by Unity's FarmHand visual asset builder.
Set TAD_UNITY_PROJECT to another project root before running it elsewhere.
"""

import math
import os
import tempfile
import uuid
from array import array

import bpy
from mathutils import Vector


CELL_PIXELS = 192
DIRECTION_COUNT = 16
MOVEMENT_FRAME_COUNT = 2
FIRE_FRAME_COUNT = 3
MOVEMENT_COLUMNS = 8
FIRE_COLUMNS = 8

PROJECT_ROOT = os.environ.get(
    "TAD_UNITY_PROJECT",
    r"C:\\Users\\Daniel\\OneDrive\\Desktop\\Unity\\TAD",
)
ART_ROOT = os.path.join(PROJECT_ROOT, "Assets", "TAD", "Art", "Farmhands")
DIRECTIONAL_ROOT = os.path.join(ART_ROOT, "Directional")
PROJECTILE_ROOT = os.path.join(ART_ROOT, "Projectiles")
SOURCE_ROOT = os.path.join(PROJECT_ROOT, "Assets", "TAD", "Art", "Source", "Farmhand")
RENDER_BUFFER_PATH = os.path.join(
    tempfile.gettempdir(),
    "tad_farmhand_render_" + uuid.uuid4().hex + ".png",
)


PALETTE = {
    "skin": (0.86, 0.43, 0.23, 1.0),
    "skin_light": (1.0, 0.66, 0.38, 1.0),
    "hair": (0.13, 0.055, 0.025, 1.0),
    "shirt": (0.12, 0.33, 0.52, 1.0),
    "overalls": (0.055, 0.17, 0.32, 1.0),
    "boot": (0.09, 0.055, 0.025, 1.0),
    "wood": (0.34, 0.16, 0.055, 1.0),
    "wood_light": (0.58, 0.30, 0.10, 1.0),
    "metal": (0.25, 0.32, 0.36, 1.0),
    "metal_light": (0.55, 0.68, 0.72, 1.0),
    "trap_red": (0.42, 0.075, 0.05, 1.0),
    "straw": (0.72, 0.51, 0.12, 1.0),
    "muzzle": (1.0, 0.66, 0.10, 1.0),
}


def ensure_directories():
    for directory in (DIRECTIONAL_ROOT, PROJECTILE_ROOT, SOURCE_ROOT):
        os.makedirs(directory, exist_ok=True)


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for datablock_collection in (bpy.data.collections, bpy.data.materials, bpy.data.images):
        # Only remove generated, unused data. Built-in datablocks are left alone.
        for datablock in list(datablock_collection):
            if datablock.users == 0:
                datablock_collection.remove(datablock)


def setup_scene():
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = CELL_PIXELS
    scene.render.resolution_y = CELL_PIXELS
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.film_transparent = True
    scene.render.image_settings.color_depth = "8"
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.use_file_extension = True

    scene.world.color = (0.035, 0.045, 0.07)

    camera_data = bpy.data.cameras.new("FarmhandSpriteCamera")
    camera = bpy.data.objects.new("FarmhandSpriteCamera", camera_data)
    scene.collection.objects.link(camera)
    camera.location = (0.0, -10.0, 7.0)
    camera_data.type = "ORTHO"
    camera_data.ortho_scale = 4.45
    point_at(camera, (0.0, 0.0, 1.45))
    scene.camera = camera

    add_area_light("Key", (4.0, -5.0, 8.0), 850.0, 5.0, (0.95, 0.88, 0.78))
    add_area_light("Fill", (-5.0, -1.0, 4.0), 400.0, 4.0, (0.45, 0.64, 1.0))
    add_area_light("Rim", (0.0, 5.0, 6.0), 650.0, 3.5, (1.0, 0.55, 0.26))


def point_at(obj, target):
    direction = Vector(target) - obj.location
    obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()


def add_area_light(name, location, energy, size, color):
    data = bpy.data.lights.new(name, "AREA")
    data.energy = energy
    data.shape = "DISK"
    data.size = size
    data.color = color
    obj = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(obj)
    obj.location = location
    point_at(obj, (0.0, 0.0, 1.0))
    return obj


def make_materials():
    materials = {}
    for name, color in PALETTE.items():
        material = bpy.data.materials.new("TAD_" + name)
        material.diffuse_color = color
        material.use_nodes = True
        principled = material.node_tree.nodes.get("Principled BSDF")
        if principled is not None:
            principled.inputs["Base Color"].default_value = color
            principled.inputs["Roughness"].default_value = 0.78
            principled.inputs["Metallic"].default_value = 0.28 if "metal" in name else 0.0
        materials[name] = material
    return materials


def new_collection(name):
    collection = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(collection)
    return collection


def move_to_collection(obj, collection):
    for current in list(obj.users_collection):
        current.objects.unlink(obj)
    collection.objects.link(obj)
    return obj


def parent_to(obj, parent):
    obj.parent = parent
    return obj


def add_ico(name, location, scale, material, collection, parent=None):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=1.0, location=location)
    obj = bpy.context.object
    obj.name = name
    obj.scale = scale
    obj.data.materials.append(material)
    move_to_collection(obj, collection)
    if parent is not None:
        parent_to(obj, parent)
    return obj


def add_uv(name, location, scale, material, collection, parent=None):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=8, ring_count=4, radius=1.0, location=location)
    obj = bpy.context.object
    obj.name = name
    obj.scale = scale
    obj.data.materials.append(material)
    move_to_collection(obj, collection)
    if parent is not None:
        parent_to(obj, parent)
    return obj


def add_cube(name, location, scale, material, collection, parent=None, rotation=None):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=location)
    obj = bpy.context.object
    obj.name = name
    obj.scale = scale
    if rotation is not None:
        obj.rotation_euler = rotation
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    bevel = obj.modifiers.new("LowPolyBevel", "BEVEL")
    bevel.width = 0.06
    bevel.segments = 1
    obj.data.materials.append(material)
    move_to_collection(obj, collection)
    if parent is not None:
        parent_to(obj, parent)
    return obj


def add_cylinder_between(name, start, end, radius, material, collection, parent=None, vertices=8):
    start = Vector(start)
    end = Vector(end)
    direction = end - start
    length = max(0.001, direction.length)
    bpy.ops.mesh.primitive_cone_add(
        vertices=vertices,
        radius1=radius,
        radius2=radius * 0.86,
        depth=length,
        location=(start + end) * 0.5,
    )
    obj = bpy.context.object
    obj.name = name
    obj.rotation_mode = "QUATERNION"
    obj.rotation_quaternion = Vector((0.0, 0.0, 1.0)).rotation_difference(direction.normalized())
    obj.data.materials.append(material)
    move_to_collection(obj, collection)
    if parent is not None:
        parent_to(obj, parent)
    return obj


def add_cone(name, location, radius1, radius2, depth, material, collection, parent=None, vertices=6):
    bpy.ops.mesh.primitive_cone_add(
        vertices=vertices,
        radius1=radius1,
        radius2=radius2,
        depth=depth,
        location=location,
    )
    obj = bpy.context.object
    obj.name = name
    obj.data.materials.append(material)
    move_to_collection(obj, collection)
    if parent is not None:
        parent_to(obj, parent)
    return obj


def add_torus(name, location, major_radius, minor_radius, material, collection, parent=None, rotation=None):
    bpy.ops.mesh.primitive_torus_add(
        major_radius=major_radius,
        minor_radius=minor_radius,
        major_segments=8,
        minor_segments=4,
        location=location,
    )
    obj = bpy.context.object
    obj.name = name
    if rotation is not None:
        obj.rotation_euler = rotation
    obj.data.materials.append(material)
    move_to_collection(obj, collection)
    if parent is not None:
        parent_to(obj, parent)
    return obj


def create_character(direction_index, phase, materials, weapon_key=None, fire_frame=None):
    body = new_collection("Render_Body")
    hair = new_collection("Render_Hair")
    outfit = new_collection("Render_Outfit")
    weapon = new_collection("Render_Weapon")

    root = bpy.data.objects.new("Farmhand_Root", None)
    bpy.context.scene.collection.objects.link(root)
    root.rotation_euler[2] = math.radians(90.0 + direction_index * (360.0 / DIRECTION_COUNT))

    step = -0.16 if phase == 0 else 0.16
    opposite_step = -step

    # The body collection deliberately includes the full neutral silhouette so it remains a usable
    # selection and placement-preview anchor even when cosmetic overlays are later swapped.
    add_ico("Head", (0.0, 0.0, 2.45), (0.47, 0.43, 0.52), materials["skin_light"], body, root)
    add_ico("Nose", (0.0, -0.42, 2.42), (0.09, 0.12, 0.10), materials["skin"], body, root)
    add_cylinder_between("LeftArm", (-0.48, 0.0, 1.82), (-0.64, step, 1.24), 0.105, materials["skin"], body, root, 6)
    add_cylinder_between("RightArm", (0.48, 0.0, 1.82), (0.64, opposite_step, 1.24), 0.105, materials["skin"], body, root, 6)
    add_ico("LeftHand", (-0.65, step, 1.18), (0.13, 0.13, 0.15), materials["skin_light"], body, root)
    add_ico("RightHand", (0.65, opposite_step, 1.18), (0.13, 0.13, 0.15), materials["skin_light"], body, root)

    # Hair is its own layer so future farmhands can choose a hairstyle independently of clothing.
    add_uv("HairCap", (0.0, 0.055, 2.72), (0.49, 0.45, 0.27), materials["hair"], hair, root)
    add_ico("HairLock", (-0.16, -0.36, 2.62), (0.19, 0.12, 0.18), materials["hair"], hair, root)

    # Outfit layer: blue work shirt, dark overalls and boots. It can be entirely replaced later.
    add_cone("WorkShirt", (0.0, 0.0, 1.70), 0.49, 0.38, 0.92, materials["shirt"], outfit, root, 6)
    add_cube("OverallsBib", (0.0, -0.42, 1.91), (0.30, 0.055, 0.31), materials["overalls"], outfit, root)
    add_cylinder_between("LeftLeg", (-0.22, 0.0, 1.22), (-0.23, step, 0.42), 0.17, materials["overalls"], outfit, root, 6)
    add_cylinder_between("RightLeg", (0.22, 0.0, 1.22), (0.23, opposite_step, 0.42), 0.17, materials["overalls"], outfit, root, 6)
    add_cube("LeftBoot", (-0.23, step - 0.08, 0.25), (0.22, 0.34, 0.15), materials["boot"], outfit, root)
    add_cube("RightBoot", (0.23, opposite_step - 0.08, 0.25), (0.22, 0.34, 0.15), materials["boot"], outfit, root)

    if weapon_key is not None:
        build_weapon_layer(weapon_key, phase, fire_frame, materials, weapon, root)

    return {"body": body, "hair": hair, "outfit": outfit, "weapon": weapon}


def build_weapon_layer(key, phase, fire_frame, materials, collection, root):
    moving = fire_frame is None
    progress = 0.0 if moving else fire_frame / max(1, FIRE_FRAME_COUNT - 1)
    front = -0.52 - (progress * 0.72 if not moving else 0.0)

    if key == "unarmed":
        # Fists move forward only during the three-frame strike, producing a readable melee punch.
        reach = 0.06 if moving else 0.46 + (progress * 0.36)
        add_ico("StrikeFist", (0.05, front - reach, 1.50), (0.23, 0.18, 0.20), materials["skin_light"], collection, root)
        if not moving and fire_frame == 1:
            add_ico("StrikeImpact", (0.05, front - reach - 0.22, 1.50), (0.16, 0.16, 0.16), materials["straw"], collection, root)
        return

    if key == "pole":
        swing = -0.45 if moving else -0.75 + (progress * 1.5)
        start = (0.14, -0.20, 1.60)
        end = (0.14 + math.sin(swing) * 0.74, -1.15 + math.cos(swing) * 0.45, 1.32)
        add_cylinder_between("Pole", start, end, 0.07, materials["wood_light"], collection, root, 8)
        add_cone("PoleTip", end, 0.14, 0.02, 0.30, materials["metal_light"], collection, root, 5)
        return

    if key == "rolling":
        offset = -0.38 if moving else -0.40 - (progress * 0.58)
        bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.32, depth=0.58, location=(0.02, offset, 1.10), rotation=(math.radians(90), 0.0, 0.0))
        roller = bpy.context.object
        roller.name = "RollingDrum"
        roller.data.materials.append(materials["wood"])
        move_to_collection(roller, collection)
        parent_to(roller, root)
        add_torus("RollerBandA", (0.02, offset - 0.24, 1.10), 0.24, 0.04, materials["metal"], collection, root, (math.radians(90), 0.0, 0.0))
        add_torus("RollerBandB", (0.02, offset + 0.24, 1.10), 0.24, 0.04, materials["metal"], collection, root, (math.radians(90), 0.0, 0.0))
        return

    if key == "trapper":
        offset = -0.33 if moving else -0.36 - (progress * 0.48)
        build_trap((0.0, offset, 1.22), 0.40, materials, collection, root, open_jaws=not moving and fire_frame >= 1)
        return

    if key == "airrifle":
        recoil = 0.0 if moving else (0.12 if fire_frame == 1 else 0.0)
        start = (0.10, -0.19 + recoil, 1.68)
        end = (0.10, -1.20 + recoil, 1.80)
        add_cylinder_between("RifleBarrel", start, end, 0.045, materials["metal_light"], collection, root, 8)
        add_cylinder_between("RifleStock", (0.10, -0.10 + recoil, 1.62), (0.10, 0.26 + recoil, 1.43), 0.095, materials["wood_light"], collection, root, 6)
        add_ico("RifleReceiver", (0.10, -0.35 + recoil, 1.65), (0.13, 0.18, 0.10), materials["metal"], collection, root)
        if not moving and fire_frame == 1:
            add_ico("MuzzleFlash", end, (0.14, 0.14, 0.14), materials["muzzle"], collection, root)
        return

    raise ValueError("Unknown weapon key: " + key)


def build_trap(center, scale, materials, collection, root, open_jaws):
    x, y, z = center
    add_cube("TrapBase", (x, y, z), (scale, scale * 0.65, 0.07), materials["metal"], collection, root)
    gap = scale * (0.72 if open_jaws else 0.38)
    for sign in (-1, 1):
        add_cylinder_between(
            "TrapJaw",
            (x + sign * gap, y - scale * 0.35, z + 0.08),
            (x + sign * gap * 0.56, y + scale * 0.38, z + (0.36 if open_jaws else 0.17)),
            0.045,
            materials["trap_red"],
            collection,
            root,
            6,
        )
    add_ico("TrapTrigger", (x, y, z + 0.13), (0.10, 0.10, 0.07), materials["metal_light"], collection, root)


def clear_model_collections():
    for collection in list(bpy.context.scene.collection.children):
        if collection.name.startswith("Render_"):
            bpy.data.collections.remove(collection)
    for obj in list(bpy.context.scene.objects):
        if obj.name in {"Farmhand_Root", "Projectile_Root"}:
            bpy.data.objects.remove(obj, do_unlink=True)


def hide_character_layers(layers, visible_layer):
    for name, collection in layers.items():
        collection.hide_render = name != visible_layer


def capture_render_pixels():
    # Blender 5.2's background Render Result does not expose pixel data. Write one temporary
    # transparent cell, reload it as an image, then compose the sheet in memory.
    bpy.context.scene.render.filepath = RENDER_BUFFER_PATH
    bpy.ops.render.render(write_still=True)
    rendered_cell = bpy.data.images.load(RENDER_BUFFER_PATH, check_existing=False)
    try:
        pixels = array("f", rendered_cell.pixels[:])
        expected_count = CELL_PIXELS * CELL_PIXELS * 4
        if len(pixels) != expected_count:
            raise RuntimeError("Unexpected rendered cell size: " + str(len(pixels)))
        return pixels
    finally:
        bpy.data.images.remove(rendered_cell)


def create_sheet(name, columns, rows):
    image = bpy.data.images.get(name)
    if image is not None:
        bpy.data.images.remove(image)
    image = bpy.data.images.new(name, width=columns * CELL_PIXELS, height=rows * CELL_PIXELS, alpha=True)
    pixels = array("f", [0.0]) * (image.size[0] * image.size[1] * 4)
    return image, pixels


def blit(sheet_pixels, sheet_width, rows, source_pixels, column, row_from_top):
    destination_y = (rows - row_from_top - 1) * CELL_PIXELS
    row_size = CELL_PIXELS * 4
    for pixel_y in range(CELL_PIXELS):
        source_offset = pixel_y * row_size
        destination_offset = ((destination_y + pixel_y) * sheet_width + (column * CELL_PIXELS)) * 4
        sheet_pixels[destination_offset: destination_offset + row_size] = source_pixels[source_offset: source_offset + row_size]


def save_sheet(image, pixels, filepath):
    image.pixels.foreach_set(pixels)
    image.filepath_raw = filepath
    image.file_format = "PNG"
    image.save()
    bpy.data.images.remove(image)


def render_directional_move_layers(materials):
    sheets = {}
    for layer in ("body", "hair", "outfit"):
        sheets[layer] = create_sheet("Farmhand_" + layer + "_Move", MOVEMENT_COLUMNS, 4)

    for direction in range(DIRECTION_COUNT):
        for frame in range(MOVEMENT_FRAME_COUNT):
            clear_model_collections()
            layers = create_character(direction, frame, materials)
            index = direction * MOVEMENT_FRAME_COUNT + frame
            column = index % MOVEMENT_COLUMNS
            row = index // MOVEMENT_COLUMNS
            for layer in ("body", "hair", "outfit"):
                hide_character_layers(layers, layer)
                image, pixels = sheets[layer]
                blit(pixels, image.size[0], 4, capture_render_pixels(), column, row)

    filenames = {
        "body": "FarmhandBody_Default_Move.png",
        "hair": "FarmhandHair_ShortBrown_Move.png",
        "outfit": "FarmhandOutfit_BlueOveralls_Move.png",
    }
    for layer, (image, pixels) in sheets.items():
        save_sheet(image, pixels, os.path.join(DIRECTIONAL_ROOT, filenames[layer]))


def render_weapon_sheets(materials):
    weapon_specs = (
        ("unarmed", "Unarmed"),
        ("pole", "Pole"),
        ("rolling", "Rolling"),
        ("trapper", "Trapper"),
        ("airrifle", "AirRifle"),
    )
    for key, title in weapon_specs:
        move_image, move_pixels = create_sheet("Farmhand_" + title + "_Move", MOVEMENT_COLUMNS, 4)
        for direction in range(DIRECTION_COUNT):
            for frame in range(MOVEMENT_FRAME_COUNT):
                clear_model_collections()
                layers = create_character(direction, frame, materials, key, None)
                hide_character_layers(layers, "weapon")
                index = direction * MOVEMENT_FRAME_COUNT + frame
                blit(
                    move_pixels,
                    move_image.size[0],
                    4,
                    capture_render_pixels(),
                    index % MOVEMENT_COLUMNS,
                    index // MOVEMENT_COLUMNS,
                )
        save_sheet(move_image, move_pixels, os.path.join(DIRECTIONAL_ROOT, "FarmhandWeapon_" + title + "_Move.png"))

        fire_image, fire_pixels = create_sheet("Farmhand_" + title + "_Fire", FIRE_COLUMNS, 6)
        for direction in range(DIRECTION_COUNT):
            for frame in range(FIRE_FRAME_COUNT):
                clear_model_collections()
                layers = create_character(direction, 0, materials, key, frame)
                hide_character_layers(layers, "weapon")
                index = direction * FIRE_FRAME_COUNT + frame
                blit(
                    fire_pixels,
                    fire_image.size[0],
                    6,
                    capture_render_pixels(),
                    index % FIRE_COLUMNS,
                    index // FIRE_COLUMNS,
                )
        save_sheet(fire_image, fire_pixels, os.path.join(DIRECTIONAL_ROOT, "FarmhandWeapon_" + title + "_Fire.png"))


def create_projectile(kind, materials):
    collection = new_collection("Render_Projectile")
    root = bpy.data.objects.new("Projectile_Root", None)
    bpy.context.scene.collection.objects.link(root)

    if kind == "unarmed":
        add_ico("GloveFist", (0.0, 0.0, 1.30), (0.42, 0.38, 0.50), materials["skin_light"], collection, root)
        add_cylinder_between("GloveWrist", (0.0, 0.0, 0.42), (0.0, 0.0, 1.02), 0.19, materials["shirt"], collection, root, 6)
    elif kind == "pole":
        add_cylinder_between("Pole", (0.0, 0.0, 0.20), (0.0, 0.0, 2.90), 0.12, materials["wood_light"], collection, root, 8)
        add_cone("PoleTip", (0.0, 0.0, 3.17), 0.25, 0.02, 0.55, materials["metal_light"], collection, root, 5)
    elif kind == "rolling":
        bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.66, depth=0.85, location=(0.0, 0.0, 1.25), rotation=(math.radians(90), 0.0, 0.0))
        drum = bpy.context.object
        drum.name = "RollingDrum"
        drum.data.materials.append(materials["wood"])
        move_to_collection(drum, collection)
        parent_to(drum, root)
        add_torus("DrumBandA", (0.0, -0.36, 1.25), 0.52, 0.08, materials["metal"], collection, root, (math.radians(90), 0.0, 0.0))
        add_torus("DrumBandB", (0.0, 0.36, 1.25), 0.52, 0.08, materials["metal"], collection, root, (math.radians(90), 0.0, 0.0))
    elif kind == "trap_closed":
        build_trap((0.0, 0.0, 1.15), 0.75, materials, collection, root, open_jaws=False)
    elif kind == "trap_armed":
        build_trap((0.0, 0.0, 1.15), 0.75, materials, collection, root, open_jaws=True)
    elif kind == "airrifle":
        add_cylinder_between("Pellet", (0.0, 0.0, 0.40), (0.0, 0.0, 2.55), 0.12, materials["metal_light"], collection, root, 8)
        add_cone("PelletTip", (0.0, 0.0, 2.75), 0.20, 0.02, 0.40, materials["muzzle"], collection, root, 8)
    else:
        raise ValueError("Unknown projectile: " + kind)
    return collection


def render_projectile_sheet(materials):
    projectile_specs = ("unarmed", "pole", "rolling", "trap_closed", "trap_armed", "airrifle")
    image, pixels = create_sheet("Farmhand_Projectiles", len(projectile_specs), 1)
    for index, projectile in enumerate(projectile_specs):
        clear_model_collections()
        create_projectile(projectile, materials)
        blit(pixels, image.size[0], 1, capture_render_pixels(), index, 0)
    save_sheet(image, pixels, os.path.join(PROJECTILE_ROOT, "FarmhandProjectiles.png"))


def save_editable_source(materials):
    clear_model_collections()
    layers = create_character(0, 0, materials, "airrifle", 1)
    for collection in layers.values():
        collection.hide_render = False
    create_projectile("unarmed", materials)
    create_projectile("pole", materials)
    create_projectile("rolling", materials)
    create_projectile("trap_closed", materials)
    create_projectile("airrifle", materials)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(SOURCE_ROOT, "Farmhand_LowPoly.blend"))


def main():
    print("TAD farmhand render: preparing output folders", flush=True)
    ensure_directories()
    print("TAD farmhand render: preparing Blender scene", flush=True)
    clear_scene()
    setup_scene()
    materials = make_materials()
    print("TAD farmhand render: body, hair and outfit frames", flush=True)
    render_directional_move_layers(materials)
    print("TAD farmhand render: weapon animations", flush=True)
    render_weapon_sheets(materials)
    print("TAD farmhand render: projectile artwork", flush=True)
    render_projectile_sheet(materials)
    print("TAD farmhand render: saving editable source", flush=True)
    save_editable_source(materials)
    if os.path.exists(RENDER_BUFFER_PATH):
        os.remove(RENDER_BUFFER_PATH)
    print("TAD Farmhand low-poly sprite sheets rendered to " + ART_ROOT)


if __name__ == "__main__":
    main()
