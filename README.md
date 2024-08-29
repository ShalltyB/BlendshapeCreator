# BlendshapeCreator
Koikatsu plugin to add custom blendshapes to meshes inside studio or maker.

## HOW TO INSTALL:
1. Download and install all the **Requirements**.
   
- [BepInEx 5](https://github.com/BepInEx/BepInEx)
- [IllusionModdingAPI](https://github.com/IllusionMods/IllusionModdingAPI)
  
2. Download the lastest BlendshapeCreator version according to your game;
   
| `KK`           | `KKS`          |      `HS2`  |
|:---------------|:----------------| :----------|
| Koikatsu       | Koikatsu Sunshine | HoneySelect2|
| Koikatsu Party |


3. Extract the **.DLL** file inside the `BepInEx\plugins` folder in your game directory.

4. Install the correct **BlendshapeCreator addon** for **Blender**.

## USAGE:

Press **( CTRL + B )** to open the UI. Or (inside Maker) check the sidebar toggle.

#### Steps:
1. Select a mesh and export it with the plugin as **.OBJ** file.
2. Import the mesh **.OBJ** file in **Blender** with the **BlendshapeCreator addon**.
3. Add shapekeys (blendshapes) to the mesh in **Blender**, and export the mesh to a **.BSDC** file with the **BlendshapeCreator addon**.
4. Import the **.BSCD** file back to Koikatsu with the mesh selected.

#### In Maker: 
- The blendshapes will be saved in the character card.
- The blendshapes sliders values will be saved in the character card, so when loading the character (ingame, studio, hscene, etc.) it will have the blendshapes values applied.

#### In Studio: 
- The blendshapes will be saved in the scene.
- The blendshapes sliders values will be saved in the scene card, to animate with **Timeline**, use **KKPE** Blendshapes sliders instead.

## IMPORTANT: 
### Don't add new geometry to the meshes (vertices, edges, faces), the meshes must have the same vertex count!
