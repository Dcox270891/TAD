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
RELOAD_FRAME_COUNT = 3
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
    "skin_medium": (0.48, 0.20, 0.09, 1.0),
    "skin_medium_light": (0.72, 0.38, 0.18, 1.0),
    "skin_deep": (0.18, 0.065, 0.025, 1.0),
    "skin_deep_light": (0.36, 0.14, 0.055, 1.0),
    "eye_white": (0.98, 0.94, 0.82, 1.0),
    "eye_dark": (0.045, 0.025, 0.018, 1.0),
    "cheek": (0.92, 0.22, 0.16, 1.0),
    "hair": (0.13, 0.055, 0.025, 1.0),
    "hair_black": (0.025, 0.02, 0.018, 1.0),
    "shirt": (0.12, 0.33, 0.52, 1.0),
    "overalls": (0.055, 0.17, 0.32, 1.0),
    "plaid_red": (0.54, 0.105, 0.065, 1.0),
    "plaid_cream": (0.98, 0.76, 0.40, 1.0),
    "field_green": (0.16, 0.34, 0.12, 1.0),
    "field_green_light": (0.39, 0.58, 0.18, 1.0),
    "trouser_tan": (0.35, 0.24, 0.11, 1.0),
    "boot": (0.09, 0.055, 0.025, 1.0),
    "wood": (0.34, 0.16, 0.055, 1.0),
    "wood_light": (0.58, 0.30, 0.10, 1.0),
    "metal": (0.25, 0.32, 0.36, 1.0),
    "metal_light": (0.55, 0.68, 0.72, 1.0),
    "trap_red": (0.42, 0.075, 0.05, 1.0),
    "straw": (0.72, 0.51, 0.12, 1.0),
    "muzzle": (1.0, 0.66, 0.10, 1.0),
    "stone": (0.33, 0.32, 0.30, 1.0),
    "stone_light": (0.62, 0.60, 0.55, 1.0),
    "brass": (0.72, 0.48, 0.12, 1.0),
    "gunmetal": (0.12, 0.15, 0.18, 1.0),
    "poison": (0.12, 0.58, 0.12, 1.0),
    "poison_light": (0.46, 1.0, 0.20, 1.0),
    "frost": (0.16, 0.64, 0.92, 1.0),
    "frost_light": (0.62, 0.93, 1.0, 1.0),
    "soil": (0.22, 0.09, 0.025, 1.0),
    "bounce": (0.76, 0.12, 0.06, 1.0),
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


def create_character(
    direction_index,
    phase,
    materials,
    weapon_key=None,
    fire_frame=None,
    reload_frame=None,
    body_variant="Default",
    hair_variant="ShortBrown",
    outfit_variant="BlueOveralls",
):
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
    body_materials = {
        "Default": ("skin_light", "skin"),
        "Medium": ("skin_medium_light", "skin_medium"),
        "Deep": ("skin_deep_light", "skin_deep"),
    }
    skin_light_key, skin_shadow_key = body_materials.get(body_variant, body_materials["Default"])
    add_ico("Head", (0.0, 0.0, 2.45), (0.47, 0.43, 0.52), materials[skin_light_key], body, root)
    add_ico("Nose", (0.0, -0.42, 2.42), (0.09, 0.12, 0.10), materials[skin_shadow_key], body, root)
    # Facial features deliberately use chunky, high-contrast facets. They are small enough to
    # keep the top-down silhouette clean, but make every randomised worker feel like a friendly
    # farm character rather than a generic mannequin at mobile scale.
    add_ico("LeftEyeWhite", (-0.16, -0.405, 2.56), (0.105, 0.045, 0.12), materials["eye_white"], body, root)
    add_ico("RightEyeWhite", (0.16, -0.405, 2.56), (0.105, 0.045, 0.12), materials["eye_white"], body, root)
    add_ico("LeftPupil", (-0.16, -0.452, 2.56), (0.052, 0.026, 0.070), materials["eye_dark"], body, root)
    add_ico("RightPupil", (0.16, -0.452, 2.56), (0.052, 0.026, 0.070), materials["eye_dark"], body, root)
    add_cube("LeftBrow", (-0.16, -0.445, 2.70), (0.13, 0.028, 0.028), materials["hair_black"], body, root, (0.0, 0.0, math.radians(-8)))
    add_cube("RightBrow", (0.16, -0.445, 2.70), (0.13, 0.028, 0.028), materials["hair_black"], body, root, (0.0, 0.0, math.radians(8)))
    add_ico("LeftCheek", (-0.30, -0.405, 2.36), (0.075, 0.025, 0.050), materials["cheek"], body, root)
    add_ico("RightCheek", (0.30, -0.405, 2.36), (0.075, 0.025, 0.050), materials["cheek"], body, root)
    add_cylinder_between("LeftArm", (-0.48, 0.0, 1.82), (-0.64, step, 1.24), 0.105, materials[skin_shadow_key], body, root, 6)
    add_cylinder_between("RightArm", (0.48, 0.0, 1.82), (0.64, opposite_step, 1.24), 0.105, materials[skin_shadow_key], body, root, 6)
    add_ico("LeftHand", (-0.65, step, 1.18), (0.13, 0.13, 0.15), materials[skin_light_key], body, root)
    add_ico("RightHand", (0.65, opposite_step, 1.18), (0.13, 0.13, 0.15), materials[skin_light_key], body, root)

    # Hair/hat stays in its own renderer layer so it can be independently combined with every
    # skin tone and outfit. A broad brim makes the hat read clearly in the top-down game camera.
    if hair_variant == "CurlyBlack":
        add_uv("CurlyHairCap", (0.0, 0.055, 2.70), (0.50, 0.46, 0.25), materials["hair_black"], hair, root)
        for lock_index, offset in enumerate(((-0.28, -0.27), (-0.10, -0.38), (0.10, -0.38), (0.28, -0.27))):
            add_ico(
                "CurlyLock" + str(lock_index),
                (offset[0], offset[1], 2.61),
                (0.16, 0.14, 0.18),
                materials["hair_black"],
                hair,
                root,
            )
    elif hair_variant == "StrawHat":
        add_cone("HatBrim", (0.0, 0.02, 2.73), 0.70, 0.64, 0.10, materials["straw"], hair, root, 8)
        add_cone("HatCrown", (0.0, 0.04, 2.89), 0.42, 0.36, 0.28, materials["straw"], hair, root, 8)
        add_torus("HatBand", (0.0, 0.04, 2.80), 0.40, 0.035, materials["wood"], hair, root)
    else:
        add_uv("HairCap", (0.0, 0.055, 2.72), (0.49, 0.45, 0.27), materials["hair"], hair, root)
        add_ico("HairLock", (-0.16, -0.36, 2.62), (0.19, 0.12, 0.18), materials["hair"], hair, root)

    # Outfit layer: every option shares the same outside silhouette so placement and movement
    # collision remain faithful to the visible farmhand while preserving gameplay balance.
    if outfit_variant == "RedPlaid":
        add_cone("PlaidShirt", (0.0, 0.0, 1.70), 0.49, 0.38, 0.92, materials["plaid_red"], outfit, root, 6)
        add_cube("PlaidStripeVertical", (0.0, -0.43, 1.70), (0.075, 0.045, 0.72), materials["plaid_cream"], outfit, root)
        add_cube("PlaidStripeHorizontal", (0.0, -0.44, 1.80), (0.68, 0.045, 0.065), materials["plaid_cream"], outfit, root)
        leg_material = materials["trouser_tan"]
        sleeve_material = materials["plaid_red"]
    elif outfit_variant == "GreenJacket":
        add_cone("FieldJacket", (0.0, 0.0, 1.70), 0.51, 0.39, 0.94, materials["field_green"], outfit, root, 6)
        add_cube("JacketZip", (0.0, -0.44, 1.70), (0.045, 0.045, 0.64), materials["field_green_light"], outfit, root)
        add_cube("JacketPocketLeft", (-0.22, -0.43, 1.48), (0.14, 0.045, 0.12), materials["field_green_light"], outfit, root)
        add_cube("JacketPocketRight", (0.22, -0.43, 1.48), (0.14, 0.045, 0.12), materials["field_green_light"], outfit, root)
        leg_material = materials["trouser_tan"]
        sleeve_material = materials["field_green"]
    else:
        add_cone("WorkShirt", (0.0, 0.0, 1.70), 0.49, 0.38, 0.92, materials["shirt"], outfit, root, 6)
        add_cube("OverallsBib", (0.0, -0.42, 1.91), (0.30, 0.055, 0.31), materials["overalls"], outfit, root)
        leg_material = materials["overalls"]
        sleeve_material = materials["shirt"]

    # Short sleeves, a sturdy belt, and a bright buckle unify all three outfit choices with the
    # chunky, inviting farm-adventure look used by the attackers and environment pieces.
    add_cylinder_between("LeftSleeve", (-0.47, 0.0, 1.84), (-0.55, step * 0.52, 1.52), 0.145, sleeve_material, outfit, root, 6)
    add_cylinder_between("RightSleeve", (0.47, 0.0, 1.84), (0.55, opposite_step * 0.52, 1.52), 0.145, sleeve_material, outfit, root, 6)
    add_cube("WorkBelt", (0.0, -0.405, 1.34), (0.47, 0.045, 0.07), materials["wood"], outfit, root)
    add_cube("BeltBuckle", (0.0, -0.462, 1.34), (0.105, 0.032, 0.105), materials["brass"], outfit, root)

    add_cylinder_between("LeftLeg", (-0.22, 0.0, 1.22), (-0.23, step, 0.42), 0.17, leg_material, outfit, root, 6)
    add_cylinder_between("RightLeg", (0.22, 0.0, 1.22), (0.23, opposite_step, 0.42), 0.17, leg_material, outfit, root, 6)
    add_cube("LeftBoot", (-0.23, step - 0.08, 0.25), (0.22, 0.34, 0.15), materials["boot"], outfit, root)
    add_cube("RightBoot", (0.23, opposite_step - 0.08, 0.25), (0.22, 0.34, 0.15), materials["boot"], outfit, root)

    if weapon_key is not None:
        build_weapon_layer(weapon_key, phase, fire_frame, reload_frame, materials, weapon, root)

    return {"body": body, "hair": hair, "outfit": outfit, "weapon": weapon}


def build_weapon_layer(key, phase, fire_frame, reload_frame, materials, collection, root):
    moving = fire_frame is None and reload_frame is None
    active_frame = fire_frame if fire_frame is not None else reload_frame
    progress = 0.0 if moving else active_frame / max(1, FIRE_FRAME_COUNT - 1)
    front = -0.52 - (progress * 0.72 if not moving else 0.0)

    if key == "unarmed":
        # Fists move forward only during the three-frame strike, producing a readable melee punch.
        reach = 0.06 if moving else 0.46 + (progress * 0.36)
        add_ico("StrikeFist", (0.05, front - reach, 1.50), (0.23, 0.18, 0.20), materials["skin_light"], collection, root)
        if not moving and fire_frame == 1:
            add_ico("StrikeImpact", (0.05, front - reach - 0.22, 1.50), (0.16, 0.16, 0.16), materials["straw"], collection, root)
        return

    if key in {"stick", "pole"}:
        swing = -0.28 if moving else -0.72 + (progress * 1.44)
        start = (0.12, -0.18, 1.56)
        length = 1.08 if key == "stick" else 1.30
        end = (0.12 + math.sin(swing) * 0.72, -0.95 + math.cos(swing) * (length - 0.55), 1.34)
        add_cylinder_between("Stick" if key == "stick" else "Pole", start, end, 0.075 if key == "stick" else 0.07, materials["wood_light"], collection, root, 8)
        if key == "stick":
            add_ico("StickKnob", end, (0.13, 0.13, 0.13), materials["wood"], collection, root)
        else:
            add_cone("PoleTip", end, 0.14, 0.02, 0.30, materials["metal_light"], collection, root, 5)
        return

    if key == "stone":
        throw_offset = 0.0 if moving else 0.52 + (progress * 0.85)
        add_ico("ThrownStone", (0.10, -0.38 - throw_offset, 1.55), (0.24, 0.24, 0.20), materials["stone"], collection, root)
        if not moving and fire_frame == 1:
            add_ico("StoneTrail", (0.10, -0.04 - throw_offset, 1.55), (0.12, 0.12, 0.10), materials["stone_light"], collection, root)
        return

    if key in {"rake", "shovel"}:
        swing = -0.36 if moving else -0.78 + (progress * 1.56)
        start = (0.12, -0.17, 1.58)
        end = (0.12 + math.sin(swing) * 0.78, -1.05 + math.cos(swing) * 0.54, 1.25)
        add_cylinder_between("ToolHandle", start, end, 0.075, materials["wood_light"], collection, root, 8)
        if key == "rake":
            add_cube("RakeHead", (end[0], end[1] - 0.06, end[2]), (0.43, 0.07, 0.07), materials["gunmetal"], collection, root)
            for tooth in (-0.30, -0.15, 0.0, 0.15, 0.30):
                add_cylinder_between(
                    "RakeTine",
                    (end[0] + tooth, end[1] - 0.04, end[2]),
                    (end[0] + tooth, end[1] - 0.25, end[2] - 0.08),
                    0.032,
                    materials["metal_light"],
                    collection,
                    root,
                    5,
                )
        else:
            add_cone("ShovelBlade", (end[0], end[1] - 0.10, end[2] - 0.10), 0.30, 0.13, 0.46, materials["metal_light"], collection, root, 5)
        return

    if key in {"rolling", "heavyroll", "bounce"}:
        if key == "heavyroll":
            radius, depth, band_material = 0.42, 0.72, materials["gunmetal"]
        elif key == "bounce":
            radius, depth, band_material = 0.34, 0.64, materials["bounce"]
        else:
            radius, depth, band_material = 0.32, 0.58, materials["metal"]
        offset = -0.38 if moving else -0.40 - (progress * 0.58)
        bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=radius, depth=depth, location=(0.02, offset, 1.10), rotation=(math.radians(90), 0.0, 0.0))
        roller = bpy.context.object
        roller.name = "RollingDrum"
        roller.data.materials.append(materials["wood"])
        move_to_collection(roller, collection)
        parent_to(roller, root)
        band_radius = max(0.18, radius - 0.08)
        add_torus("RollerBandA", (0.02, offset - (depth * 0.40), 1.10), band_radius, 0.045, band_material, collection, root, (math.radians(90), 0.0, 0.0))
        add_torus("RollerBandB", (0.02, offset + (depth * 0.40), 1.10), band_radius, 0.045, band_material, collection, root, (math.radians(90), 0.0, 0.0))
        if key == "bounce":
            add_cone("BounceSpring", (0.02, offset + 0.03, 1.56), 0.16, 0.08, 0.38, materials["bounce"], collection, root, 6)
        return

    if key in {"trapper", "poisontrapper", "slowtrapper"}:
        offset = -0.33 if moving else -0.36 - (progress * 0.48)
        jaw_material = materials["trap_red"]
        accent_material = materials["metal_light"]
        if key == "poisontrapper":
            jaw_material = materials["poison"]
            accent_material = materials["poison_light"]
        elif key == "slowtrapper":
            jaw_material = materials["frost"]
            accent_material = materials["frost_light"]
        build_trap(
            (0.0, offset, 1.22),
            0.40,
            materials,
            collection,
            root,
            open_jaws=not moving and fire_frame is not None and fire_frame >= 1,
            jaw_material=jaw_material,
            accent_material=accent_material,
        )
        return

    if key in {"airrifle", "semiautomatic", "pistol"}:
        if key == "pistol":
            build_pistol(progress, moving, fire_frame, reload_frame, materials, collection, root)
        else:
            build_rifle(key, progress, moving, fire_frame, reload_frame, materials, collection, root)
        return

    raise ValueError("Unknown weapon key: " + key)


def build_rifle(key, progress, moving, fire_frame, reload_frame, materials, collection, root):
    recoil = 0.0 if moving or reload_frame is not None else (0.12 if fire_frame == 1 else 0.0)
    reload_lift = 0.0 if reload_frame is None else 0.12 + (0.12 * math.sin(progress * math.pi))
    barrel_length = 1.02 if key == "airrifle" else 0.88
    start = (0.10, -0.19 + recoil + reload_lift, 1.68)
    end = (0.10, -0.19 - barrel_length + recoil + reload_lift, 1.80)
    add_cylinder_between("RifleBarrel", start, end, 0.045, materials["metal_light"], collection, root, 8)
    add_cylinder_between("RifleStock", (0.10, -0.10 + recoil + reload_lift, 1.62), (0.10, 0.26 + recoil + reload_lift, 1.43), 0.095, materials["wood_light"], collection, root, 6)
    add_ico("RifleReceiver", (0.10, -0.35 + recoil + reload_lift, 1.65), (0.13, 0.18, 0.10), materials["gunmetal"], collection, root)
    if key == "semiautomatic":
        add_cube("Magazine", (0.10, -0.28 + reload_lift, 1.48 - (0.13 if reload_frame is not None else 0.0)), (0.09, 0.11, 0.21), materials["brass"], collection, root)
    if reload_frame is not None:
        add_cylinder_between("ReloadLever", (0.10, -0.45 + reload_lift, 1.70), (0.10, -0.22 + reload_lift, 1.92), 0.035, materials["metal"], collection, root, 6)
    elif not moving and fire_frame == 1:
        add_ico("MuzzleFlash", end, (0.14, 0.14, 0.14), materials["muzzle"], collection, root)


def build_pistol(progress, moving, fire_frame, reload_frame, materials, collection, root):
    recoil = 0.0 if moving or reload_frame is not None else (0.10 if fire_frame == 1 else 0.0)
    reload_lift = 0.0 if reload_frame is None else 0.10 + (0.09 * math.sin(progress * math.pi))
    start = (0.08, -0.24 + recoil + reload_lift, 1.62)
    end = (0.08, -0.82 + recoil + reload_lift, 1.66)
    add_cube("PistolSlide", (0.08, -0.50 + recoil + reload_lift, 1.66), (0.11, 0.34, 0.08), materials["gunmetal"], collection, root)
    add_cylinder_between("PistolBarrel", start, end, 0.042, materials["metal_light"], collection, root, 8)
    add_cylinder_between("PistolGrip", (0.08, -0.27 + recoil + reload_lift, 1.58), (0.08, -0.06 + recoil + reload_lift, 1.22), 0.105, materials["wood_light"], collection, root, 6)
    if reload_frame is not None:
        add_cube("PistolMagazine", (0.08, -0.06 + reload_lift, 1.16 - (0.18 * (1.0 - progress))), (0.07, 0.09, 0.22), materials["brass"], collection, root)
    elif not moving and fire_frame == 1:
        add_ico("PistolMuzzleFlash", end, (0.12, 0.12, 0.12), materials["muzzle"], collection, root)


def build_trap(
    center,
    scale,
    materials,
    collection,
    root,
    open_jaws,
    jaw_material=None,
    accent_material=None,
):
    jaw_material = jaw_material or materials["trap_red"]
    accent_material = accent_material or materials["metal_light"]
    x, y, z = center
    add_cube("TrapBase", (x, y, z), (scale, scale * 0.65, 0.07), materials["metal"], collection, root)
    gap = scale * (0.72 if open_jaws else 0.38)
    for sign in (-1, 1):
        add_cylinder_between(
            "TrapJaw",
            (x + sign * gap, y - scale * 0.35, z + 0.08),
            (x + sign * gap * 0.56, y + scale * 0.38, z + (0.36 if open_jaws else 0.17)),
            0.045,
            jaw_material,
            collection,
            root,
            6,
        )
    add_ico("TrapTrigger", (x, y, z + 0.13), (0.10, 0.10, 0.07), accent_material, collection, root)


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
    layer_specs = (
        ("body", "FarmhandBody", ("Default", "Medium", "Deep")),
        ("hair", "FarmhandHair", ("ShortBrown", "CurlyBlack", "StrawHat")),
        ("outfit", "FarmhandOutfit", ("BlueOveralls", "RedPlaid", "GreenJacket")),
    )
    for layer, filename_prefix, variants in layer_specs:
        for variant in variants:
            image, pixels = create_sheet(
                filename_prefix + "_" + variant + "_Move",
                MOVEMENT_COLUMNS,
                4,
            )
            for direction in range(DIRECTION_COUNT):
                for frame in range(MOVEMENT_FRAME_COUNT):
                    clear_model_collections()
                    layers = create_character(
                        direction,
                        frame,
                        materials,
                        **{layer + "_variant": variant},
                    )
                    hide_character_layers(layers, layer)
                    index = direction * MOVEMENT_FRAME_COUNT + frame
                    blit(
                        pixels,
                        image.size[0],
                        4,
                        capture_render_pixels(),
                        index % MOVEMENT_COLUMNS,
                        index // MOVEMENT_COLUMNS,
                    )
            save_sheet(
                image,
                pixels,
                os.path.join(DIRECTIONAL_ROOT, filename_prefix + "_" + variant + "_Move.png"),
            )


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


def render_upgrade_weapon_sheets(materials):
    """Render the two free level-two choices and all level-eight weapon evolutions.

    These use the exact same 16-directional sheet convention as the original five weapon
    types, so the runtime can remain entirely data driven when a defender changes weapon.
    """
    weapon_specs = (
        ("stick", "Stick"),
        ("stone", "Stone"),
        ("rake", "Rake"),
        ("shovel", "Shovel"),
        ("heavyroll", "HeavyRoll"),
        ("bounce", "Bounce"),
        ("semiautomatic", "SemiAutomatic"),
        ("pistol", "Pistol"),
        ("poisontrapper", "PoisonTrapper"),
        ("slowtrapper", "SlowTrapper"),
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

    # The base Air Rifle now reloads too, so it shares the same animated reload presentation as
    # its level-eight rifle descendants.
    reload_specs = (
        ("airrifle", "AirRifle"),
        ("semiautomatic", "SemiAutomatic"),
        ("pistol", "Pistol"),
    )
    for key, title in reload_specs:
        reload_image, reload_pixels = create_sheet("Farmhand_" + title + "_Reload", FIRE_COLUMNS, 6)
        for direction in range(DIRECTION_COUNT):
            for frame in range(RELOAD_FRAME_COUNT):
                clear_model_collections()
                layers = create_character(direction, 0, materials, key, None, frame)
                hide_character_layers(layers, "weapon")
                index = direction * RELOAD_FRAME_COUNT + frame
                blit(
                    reload_pixels,
                    reload_image.size[0],
                    6,
                    capture_render_pixels(),
                    index % FIRE_COLUMNS,
                    index // FIRE_COLUMNS,
                )
        save_sheet(reload_image, reload_pixels, os.path.join(DIRECTIONAL_ROOT, "FarmhandWeapon_" + title + "_Reload.png"))


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


def create_upgrade_projectile(kind, materials):
    """Create one isolated low-poly projectile, impact or trap-presentation cell.

    Each object is kept intentionally chunky: these sprites are viewed at gameplay scale on a
    mobile screen, where a clear silhouette matters more than small surface detail.
    """
    collection = new_collection("Render_UpgradeProjectile")
    root = bpy.data.objects.new("Projectile_Root", None)
    bpy.context.scene.collection.objects.link(root)

    if kind == "stick":
        add_cylinder_between("StickProjectile", (0.0, 0.0, 0.20), (0.0, 0.0, 2.45), 0.12, materials["wood_light"], collection, root, 8)
        add_ico("StickKnob", (0.0, 0.0, 2.62), (0.20, 0.20, 0.20), materials["wood"], collection, root)
    elif kind == "stone":
        add_ico("StoneProjectile", (0.0, 0.0, 1.35), (0.68, 0.60, 0.54), materials["stone"], collection, root)
        add_ico("StoneHighlight", (-0.17, -0.14, 1.58), (0.22, 0.18, 0.16), materials["stone_light"], collection, root)
    elif kind in {"rake", "shovel"}:
        add_cylinder_between("ToolHandle", (0.0, 0.0, 0.18), (0.0, 0.0, 2.35), 0.10, materials["wood_light"], collection, root, 8)
        if kind == "rake":
            add_cube("RakeHead", (0.0, 0.0, 2.55), (0.62, 0.11, 0.10), materials["gunmetal"], collection, root)
            for tooth in (-0.44, -0.22, 0.0, 0.22, 0.44):
                add_cylinder_between(
                    "RakeTine",
                    (tooth, 0.0, 2.53),
                    (tooth, -0.04, 2.92),
                    0.040,
                    materials["metal_light"],
                    collection,
                    root,
                    5,
                )
        else:
            add_cone("ShovelBlade", (0.0, 0.0, 2.62), 0.48, 0.16, 0.72, materials["metal_light"], collection, root, 5)
    elif kind in {"heavyroll", "bounce"}:
        radius = 0.78 if kind == "heavyroll" else 0.62
        depth = 0.96 if kind == "heavyroll" else 0.82
        band_material = materials["gunmetal"] if kind == "heavyroll" else materials["bounce"]
        bpy.ops.mesh.primitive_cylinder_add(
            vertices=8,
            radius=radius,
            depth=depth,
            location=(0.0, 0.0, 1.32),
            rotation=(math.radians(90), 0.0, 0.0),
        )
        drum = bpy.context.object
        drum.name = "HeavyRoll" if kind == "heavyroll" else "BounceRoll"
        drum.data.materials.append(materials["wood"])
        move_to_collection(drum, collection)
        parent_to(drum, root)
        add_torus("RollBandA", (0.0, -depth * 0.40, 1.32), radius * 0.78, 0.09, band_material, collection, root, (math.radians(90), 0.0, 0.0))
        add_torus("RollBandB", (0.0, depth * 0.40, 1.32), radius * 0.78, 0.09, band_material, collection, root, (math.radians(90), 0.0, 0.0))
        if kind == "bounce":
            add_cone("BounceSpring", (0.0, 0.0, 2.12), 0.26, 0.12, 0.60, materials["bounce"], collection, root, 6)
    elif kind in {"semiautomatic_bullet", "pistol_bullet"}:
        length = 2.72 if kind == "semiautomatic_bullet" else 2.16
        body_material = materials["brass"] if kind == "semiautomatic_bullet" else materials["metal_light"]
        add_cylinder_between("Bullet", (0.0, 0.0, 0.25), (0.0, 0.0, length), 0.13, body_material, collection, root, 8)
        add_cone("BulletTip", (0.0, 0.0, length + 0.23), 0.23, 0.02, 0.48, materials["muzzle"], collection, root, 8)
    elif kind in {"poison_closed", "poison_armed", "slow_closed", "slow_armed"}:
        is_poison = kind.startswith("poison")
        jaw_material = materials["poison"] if is_poison else materials["frost"]
        accent_material = materials["poison_light"] if is_poison else materials["frost_light"]
        build_trap(
            (0.0, 0.0, 1.20),
            0.76,
            materials,
            collection,
            root,
            open_jaws=kind.endswith("armed"),
            jaw_material=jaw_material,
            accent_material=accent_material,
        )
        add_ico("TrapVial", (0.0, 0.0, 1.54), (0.20, 0.20, 0.28), accent_material, collection, root)
    elif kind.startswith("poison_explosion") or kind.startswith("slow_explosion"):
        is_poison = kind.startswith("poison")
        frame = int(kind[-1])
        base_material = materials["poison"] if is_poison else materials["frost"]
        accent_material = materials["poison_light"] if is_poison else materials["frost_light"]
        radius = 0.40 + (frame * 0.28)
        add_uv("DetonationCore", (0.0, 0.0, 0.22), (radius, radius, 0.10), base_material, collection, root)
        for index in range(6 + frame * 2):
            angle = (math.tau * index) / (6 + frame * 2)
            distance = radius * (0.72 + frame * 0.14)
            add_ico(
                "DetonationShard",
                (math.cos(angle) * distance, math.sin(angle) * distance, 0.26),
                (0.15 + frame * 0.04, 0.15 + frame * 0.04, 0.11),
                accent_material,
                collection,
                root,
            )
    elif kind.startswith("bounce_impact"):
        frame = int(kind[-1])
        radius = 0.48 + (frame * 0.34)
        add_uv("BounceImpact", (0.0, 0.0, 0.16), (radius, radius, 0.075), materials["bounce"], collection, root)
        for index in range(4 + frame * 3):
            angle = (math.tau * index) / (4 + frame * 3)
            distance = radius * (0.66 + frame * 0.18)
            add_ico(
                "BounceSpark",
                (math.cos(angle) * distance, math.sin(angle) * distance, 0.22),
                (0.10 + frame * 0.05, 0.10 + frame * 0.05, 0.08),
                materials["muzzle"],
                collection,
                root,
            )
    else:
        raise ValueError("Unknown upgrade projectile: " + kind)
    return collection


def render_upgrade_projectile_sheet(materials):
    """Render all dedicated level-two/final projectile and effect frames in one compact atlas."""
    projectile_specs = (
        ("stick", "upgrade_projectile_stick"),
        ("stone", "upgrade_projectile_stone"),
        ("rake", "upgrade_projectile_rake"),
        ("shovel", "upgrade_projectile_shovel"),
        ("heavyroll", "upgrade_projectile_heavy_roll"),
        ("bounce", "upgrade_projectile_bounce"),
        ("semiautomatic_bullet", "upgrade_projectile_semi_automatic"),
        ("pistol_bullet", "upgrade_projectile_pistol"),
        ("poison_closed", "upgrade_projectile_poison_closed"),
        ("poison_armed", "upgrade_projectile_poison_armed"),
        ("slow_closed", "upgrade_projectile_slow_closed"),
        ("slow_armed", "upgrade_projectile_slow_armed"),
        ("poison_explosion0", "upgrade_projectile_poison_explosion0"),
        ("poison_explosion1", "upgrade_projectile_poison_explosion1"),
        ("poison_explosion2", "upgrade_projectile_poison_explosion2"),
        ("slow_explosion0", "upgrade_projectile_slow_explosion0"),
        ("slow_explosion1", "upgrade_projectile_slow_explosion1"),
        ("slow_explosion2", "upgrade_projectile_slow_explosion2"),
        ("bounce_impact0", "upgrade_projectile_bounce_impact0"),
        ("bounce_impact1", "upgrade_projectile_bounce_impact1"),
        ("bounce_impact2", "upgrade_projectile_bounce_impact2"),
    )
    columns = 6
    rows = 4
    image, pixels = create_sheet("Farmhand_UpgradeProjectiles", columns, rows)
    for index, (projectile, _) in enumerate(projectile_specs):
        clear_model_collections()
        create_upgrade_projectile(projectile, materials)
        blit(pixels, image.size[0], rows, capture_render_pixels(), index % columns, index // columns)
    save_sheet(image, pixels, os.path.join(PROJECTILE_ROOT, "DefenderUpgradeProjectiles.png"))


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
    upgrade_only = os.environ.get("TAD_RENDER_UPGRADES_ONLY") == "1"
    cosmetics_only = os.environ.get("TAD_RENDER_COSMETICS_ONLY") == "1"
    if cosmetics_only:
        print("TAD farmhand render: body, hair and outfit cosmetic frames", flush=True)
        render_directional_move_layers(materials)
    elif upgrade_only:
        print("TAD farmhand render: level-two and final-upgrade weapon animations", flush=True)
        render_upgrade_weapon_sheets(materials)
        print("TAD farmhand render: upgrade projectile artwork and effect frames", flush=True)
        render_upgrade_projectile_sheet(materials)
    else:
        print("TAD farmhand render: body, hair and outfit frames", flush=True)
        render_directional_move_layers(materials)
        print("TAD farmhand render: base weapon animations", flush=True)
        render_weapon_sheets(materials)
        print("TAD farmhand render: base projectile artwork", flush=True)
        render_projectile_sheet(materials)
    if os.environ.get("TAD_SAVE_EDITABLE_SOURCE", "1") != "0":
        print("TAD farmhand render: saving editable source", flush=True)
        save_editable_source(materials)
    if os.path.exists(RENDER_BUFFER_PATH):
        os.remove(RENDER_BUFFER_PATH)
    print("TAD Farmhand low-poly sprite sheets rendered to " + ART_ROOT)


if __name__ == "__main__":
    main()
