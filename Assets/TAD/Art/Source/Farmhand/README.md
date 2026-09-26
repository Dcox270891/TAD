# Farmhand visual source

`Farmhand_LowPoly.blend` is the editable low-poly source for the default farmhand and the five projectile designs.

The Unity farmhand prefab is assembled from independent **Body**, **Hair**, **Outfit**, and **Weapon** sprite layers. New cosmetics should follow the existing 16-direction, two-frame movement sheet layout (8 columns x 4 rows, 192 px cells). Weapon firing sheets use three frames per direction (8 columns x 6 rows).

`FarmhandSpriteSheetRenderer.py` is the procedural Blender source used to generate the default artwork and matching sheets. It recreates its scene when run, so copy it before making broad experimental changes. Run it through Blender with `TAD_UNITY_PROJECT` set to the Unity project folder if you want to generate the sheets in another checkout.

After replacing or adding artwork, use **TAD > Defenders > Rebuild Farmhand Visuals** in Unity. New hair or outfit assets can then be combined into new `FarmHandAppearanceDefinition` assets without changing gameplay code.