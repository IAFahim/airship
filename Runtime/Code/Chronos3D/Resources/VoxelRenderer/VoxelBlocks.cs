using Assets.Chronos.VoxelRenderer;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.Profiling;

using VoxelData = System.UInt16;
using BlockId = System.UInt16;

[LuauAPI]
public class VoxelBlocks
{

    public enum TileSizes :int
    {
        TileSize1x1x1=0,
        TileSize2x2x2=1,
        TileSize3x3x3=2,
        TileSize4x4x4=3,
        Max = 4,
    }
    public static Dictionary<int, Vector3> meshTileOffsets = new Dictionary<int, Vector3>()
    {
        { (int)TileSizes.TileSize1x1x1,Vector3.zero },
        { (int)TileSizes.TileSize2x2x2,(new Vector3(2,2,2)/2.0f) + new Vector3(-0.5f,-0.5f,-0.5f) },
        { (int)TileSizes.TileSize3x3x3,(new Vector3(3,3,3)/2.0f) + new Vector3(-0.5f,-0.5f,-0.5f) },
        { (int)TileSizes.TileSize4x4x4,(new Vector3(4,4,4)/2.0f) + new Vector3(-0.5f,-0.5f,-0.5f) },
    };
    public static Dictionary<int, Vector3Int> meshTileSizes = new()
    {
        { (int)TileSizes.TileSize1x1x1, new Vector3Int(1,1,1) },
        { (int)TileSizes.TileSize2x2x2, new Vector3Int(2,2,2) },
        { (int)TileSizes.TileSize3x3x3, new Vector3Int(3,3,3) },
        { (int)TileSizes.TileSize4x4x4, new Vector3Int(4,4,4) },
    };

    //Make a string table for these
    public static string[] TileSizeNames = new string[]
    {
        "1x1x1",
        "2x2x2",
        "3x3x3",
        "4x4x4",
    };


    public class BlockDefinition
    {
        public string name { get; set; }
        public string topTexture { get; set; }

        public string topMaterial { get; set; }
        public string material { get; set; }

        public string bottomTexture { get; set; }
        public string sideTexture { get; set; }

        public string meshTexture { get; set; }
        public string meshPath { get; set; }
        public string meshPathLod { get; set; }

        public byte index { get; set; }

        public bool fake = false;

        public float metallic = 0;
        public float roughness = 1;
        public float normalScale = 1;
        public float emissive = 0;
        public float brightness = 1;

        public bool usesTiles = false;
        public bool solid = true;
        
        public MeshCopy mesh = null;
        public MeshCopy meshLod = null;

        public Dictionary<int, MeshCopy> meshTiles = new();
        public List<int> meshTileProcessingOrder = new();
        
        public bool detail = false;

        public string meshTexturePath = "";
        public string topTexturePath = "";
        public string sideTexturePath = "";
        public string bottomTexturePath = "";

        public Texture2D editorTexture; //Null in release

        public Rect topUvs;
        public Rect bottomUvs;
        public Rect sideUvs;
        public bool doOcclusion = true;

        public string[] materials = new string[6];
        public string meshMaterialName;

        public Color[] averageColor = new Color[3];//x y z

        [CanBeNull]
        public string[] minecraftConversions;
 
        public Rect GetUvsForFace(int i)
        {
            switch (i)
            {
                default: return sideUvs;
                case 1: return sideUvs;
                case 2: return sideUvs;
                case 3: return sideUvs;
                case 4: return topUvs;
                case 5: return bottomUvs;
            }
        }
    }

    public TexturePacker atlas = new TexturePacker();
    public Dictionary<string, Material> materials = new();

    Dictionary<string, TexturePacker.TextureSet> temporaryTextures = new();
    public Dictionary<BlockId, BlockDefinition> loadedBlocks = new();

    public string rootAssetPath;
    public List<string> m_bundlePaths = null;
    public BlockDefinition GetBlock(BlockId index)
    {
        loadedBlocks.TryGetValue(index, out BlockDefinition value);
        return value;
    }

    public BlockDefinition GetBlockDefinitionFromIndex(int index) {
        return GetBlock((ushort)index);
    }

    public BlockDefinition GetBlockDefinitionFromName(string name)
    {
        return GetBlock(GetBlockId(name));
    }

    public BlockId GetBlockId(string name)
    {
        foreach (KeyValuePair<BlockId, BlockDefinition> pair in loadedBlocks)
        {
            if (pair.Value.name == name)
            {
                return pair.Key;
            }
        }
        return 0;
    }

    //Destructor
    ~VoxelBlocks()
    {
        atlas.Dispose();

        //destroy all the materials
        foreach (KeyValuePair<string, Material> pair in materials)
        {
            UnityEngine.Object.Destroy(pair.Value);
        }
    }


    public void Load(string contentsOfBlockDefines, bool loadTexturesDirectlyFromDisk = false)
    {
        Profiler.BeginSample("VoxelBlocks.Load");
        temporaryTextures.Clear();
        atlas = new TexturePacker();
        
        //Add air
        BlockDefinition airBlock = new BlockDefinition();
        airBlock.solid = false;
        airBlock.name = "air";
        loadedBlocks.Add(0, airBlock);
        
        Dictionary<byte, BlockDefinition> blocks = new();
        XmlDocument xmlDoc = new XmlDocument();
        
        xmlDoc.LoadXml(contentsOfBlockDefines);

        rootAssetPath = xmlDoc["Blocks"]?["RootAssetPath"]?.InnerText;
        if (rootAssetPath == null)
        {
            rootAssetPath = "Shared/Resources/VoxelWorld";
        } else
        {
            Debug.Log("Using RootAssetPath \"" + rootAssetPath + "\"");
        }

        XmlNodeList blockList = xmlDoc.GetElementsByTagName("Block");

        Profiler.BeginSample("XmlParsing");
        foreach (XmlNode blockNode in blockList)
        {
            BlockDefinition block = new BlockDefinition();
            block.name = blockNode["Name"].InnerText;

            block.meshTexture = blockNode["MeshTexture"] != null ? blockNode["MeshTexture"].InnerText : "";
            block.topTexture = blockNode["TopTexture"] != null ? blockNode["TopTexture"].InnerText : "";
            block.topMaterial = blockNode["TopMaterial"] != null ? blockNode["TopMaterial"].InnerText : "";
            block.material = blockNode["Material"] != null ? blockNode["Material"].InnerText : "";

            block.bottomTexture = blockNode["BottomTexture"] != null ? blockNode["BottomTexture"].InnerText : "";

            block.sideTexture = blockNode["SideTexture"] != null ? blockNode["SideTexture"].InnerText : "";

            block.index = byte.Parse(blockNode["Index"].InnerText);
            block.metallic = blockNode["Metallic"] != null ? float.Parse(blockNode["Metallic"].InnerText) : 0;
            block.roughness = blockNode["Roughness"] != null ? float.Parse(blockNode["Roughness"].InnerText) : 1;
            block.emissive = blockNode["Emissive"] != null ? float.Parse(blockNode["Emissive"].InnerText) : 0;

            block.brightness = blockNode["Brightness"] != null ? float.Parse(blockNode["Brightness"].InnerText) : 1;

            block.solid = blockNode["Solid"] != null ? bool.Parse(blockNode["Solid"].InnerText) : true;
            block.meshPath = blockNode["Mesh"] != null ? blockNode["Mesh"].InnerText : null;
            block.meshPathLod = blockNode["MeshLod"] != null ? blockNode["MeshLod"].InnerText : null;
            block.normalScale = blockNode["NormalScale"] != null ? float.Parse(blockNode["NormalScale"].InnerText) : 1;

            block.detail = blockNode["Detail"] != null ? bool.Parse(blockNode["Detail"].InnerText) : true;

            if (blockNode["Minecraft"] != null)
            {
                string text = blockNode["Minecraft"].InnerText;
                string[] split = text.Split(",");
                block.minecraftConversions = split;
            } else
            {
                block.minecraftConversions = null;
            }

            if (blockNode["Fake"] != null && bool.Parse(blockNode["Fake"].InnerText))
            {
                block.fake = true;
                block.solid = false;
            }

            string tileBase = blockNode["TileBase"] != null ? blockNode["TileBase"].InnerText : "";

            if (tileBase != "")
            {
                //Do the Tiles 
                for (int i = 0; i < (int)TileSizes.Max; i++)
                {
                    string meshPath = $"{rootAssetPath}/Meshes/" + tileBase + TileSizeNames[i];

                    MeshCopy meshCopy = new MeshCopy(meshPath);
                    if (meshCopy == null)
                    {
                        //Debug.LogWarning("Could not find tile mesh at " + meshPath);
                        if (i == 0)
                        {
                            //Dont look for any more if the 1x1 is missing
                            break;
                        }
                    }
                    else
                    {
                        block.meshTiles.Add(i, meshCopy);
                        block.usesTiles = true;
                    }
                     
                    
                }
            }

            //iterate through the Tilesizes backwards
            for (int i = (int)TileSizes.Max-1;i>0;i--)
            {
                bool found = block.meshTiles.TryGetValue(i, out MeshCopy val);
                if (found && i > 0)
                {
                    block.meshTileProcessingOrder.Add(i);
                    
                }
            }
            
            //Check for duplicate
            if (blocks.ContainsKey(block.index))
            {
                Debug.LogError("Duplicate block index: " + block.index + " for block: " + block.name + " Existing block name is" + blocks[block.index].name);
                continue;
            }

            blocks.Add(block.index, block);


            if (block.meshPath != null)
            {
                block.meshPath = rootAssetPath + "/Meshes/" + block.meshPath;
                block.mesh = new MeshCopy(block.meshPath);

                //Texture should be adjacent to the mesh
                if (block.meshTexture != "")
                {
                    string pathWithoutFilename = block.meshPath.Substring(0, block.meshPath.LastIndexOf('/'));
                    block.meshTexturePath = Path.Combine(pathWithoutFilename, block.meshTexture);
                    if (temporaryTextures.ContainsKey(block.meshTexturePath) == false)
                    {
                        var tex = LoadTexture(loadTexturesDirectlyFromDisk, block.meshTexturePath, block.roughness, block.metallic, block.normalScale, block.emissive, block.brightness);
    #if UNITY_EDITOR
                        //prefer the mesh texture..
                        block.editorTexture = tex.diffuse;
    #endif
                    }
                }
            }

            if (block.meshPathLod != null)
            {
                block.meshPathLod = $"{rootAssetPath}/Meshes/" + block.meshPathLod;
                block.meshLod = new MeshCopy(block.meshPathLod);
            }

            if (block.sideTexture != "")
            {
                block.sideTexturePath = $"{rootAssetPath}/Textures/" + block.sideTexture;
                if (temporaryTextures.ContainsKey(block.sideTexturePath) == false)
                {
                    var tex = LoadTexture(loadTexturesDirectlyFromDisk, block.sideTexturePath, block.roughness, block.metallic, block.normalScale, block.emissive, block.brightness);
#if UNITY_EDITOR
                    //prefer the side texture..
                    block.editorTexture = tex.diffuse;
#endif
                }
            }

            if (block.topTexture != "")
            {
                block.topTexturePath = $"{rootAssetPath}/Textures/" + block.topTexture;
                if (temporaryTextures.ContainsKey(block.topTexturePath) == false)
                {
                    var tex = LoadTexture(loadTexturesDirectlyFromDisk, block.topTexturePath, block.roughness, block.metallic, block.normalScale, block.emissive, block.brightness);
#if UNITY_EDITOR
                    if (block.editorTexture == null)
                    {
                        block.editorTexture = tex.diffuse;
                    }
#endif
                }
            }


            if (block.bottomTexture != "")
            {
                block.bottomTexturePath = $"{rootAssetPath}/Textures/" + block.bottomTexture;
                if (temporaryTextures.ContainsKey(block.bottomTexturePath) == false)
                {
                    var tex = LoadTexture(loadTexturesDirectlyFromDisk, block.bottomTexturePath, block.roughness, block.metallic, block.normalScale, block.emissive, block.brightness);
#if UNITY_EDITOR
                    if (block.editorTexture == null)
                    {
                        block.editorTexture = tex.diffuse;
                    }
#endif
                }
            }

            loadedBlocks[block.index] = block;
        }

        Profiler.EndSample();
        Debug.Log("Loaded " + blocks.Count + " blocks");


        //Create atlas
        int numMips = 8;    //We use a restricted number of mipmaps because after that we start spilling into other regions and you get distant shimmers
        int defaultTextureSize = 64;
        int padding = defaultTextureSize / 2;
        atlas.PackTextures(temporaryTextures, padding, 2048, 2048, numMips, defaultTextureSize);
        temporaryTextures.Clear();

        //create the materials
        Profiler.BeginSample("CreateMaterials");
        foreach (var blockRec in loadedBlocks)
        {
            for (int i = 0; i < 6; i++)
            {
                blockRec.Value.materials[i] = "atlas";
            }
           
            if (blockRec.Value.material != "")
            {
                string matName = blockRec.Value.material;
              
                if (matName != null)
                {
                    materials.TryGetValue(matName, out Material sourceMat);
                    if (sourceMat == null)
                    {
                        sourceMat = AssetBridge.LoadAssetInternal<Material>($"{rootAssetPath}/Materials/" + matName + ".mat", true);
                        //See if sourceMat.EXPLICIT_MAPS_ON is false
                        if (sourceMat.IsKeywordEnabled("EXPLICIT_MAPS_ON") == false)
                        {
                           sourceMat.SetTexture("_MainTex", atlas.diffuse);
                           sourceMat.SetTexture("_NormalTex", atlas.normals);
                        }
                        
                        materials[matName] = sourceMat;
                    }
                    if (sourceMat != null)
                    {
                        blockRec.Value.materials[0] = matName;
                        blockRec.Value.materials[1] = matName;
                        blockRec.Value.materials[2] = matName;
                        blockRec.Value.materials[3] = matName;
                        blockRec.Value.materials[4] = matName;
                        blockRec.Value.materials[5] = matName;
                    }

                    if (sourceMat != null && blockRec.Value.mesh != null)
                    {
                        blockRec.Value.mesh.meshMaterial = sourceMat;
                        blockRec.Value.mesh.meshMaterialName = sourceMat.name;
                    }
                    if (sourceMat != null && blockRec.Value.meshLod != null)
                    {
                        blockRec.Value.meshLod.meshMaterial = sourceMat;
                        blockRec.Value.meshLod.meshMaterialName = sourceMat.name;
                    }
                    

                }

            }

            if (blockRec.Value.topMaterial != "")
            {
                string matName = blockRec.Value.topMaterial;

                if (matName != null)
                {
                    materials.TryGetValue(matName, out Material sourceMat);
                    if (sourceMat == null)
                    {
                        sourceMat = AssetBridge.LoadAssetInternal<Material>($"{rootAssetPath}/Materials/" + matName + ".mat", true);
                        //See if sourceMat.EXPLICIT_MAPS is false
                        if (sourceMat.IsKeywordEnabled("EXPLICIT_MAPS_ON") == false)
                        {
                           sourceMat.SetTexture("_MainTex", atlas.diffuse);
                           sourceMat.SetTexture("_NormalTex", atlas.normals);
                        }
                        materials[matName] = sourceMat;
                    }
                    if (sourceMat != null)
                    {
                   
                        
                        blockRec.Value.materials[4] = matName;
                    }
                }

            }


            if (blockRec.Value.meshTexture != "")
            {
                blockRec.Value.meshMaterialName = "atlas";
                //blockRec.Value.meshMaterial = Resources.Load<Material>("atlas");
            }
            else
            {
                //MeshCopy has already loaded its material
            }

        }
        Profiler.EndSample();

        //fullPBR, needs two materials, one for opaque and one for transparencies
        Material atlasMaterial;
        atlasMaterial = new Material(Shader.Find("Chronos/WorldShaderPBR"));
        atlasMaterial.SetTexture("_MainTex", atlas.diffuse);
        atlasMaterial.SetTexture("_NormalTex", atlas.normals);

        //Set appropriate settings for the atlas  (vertex light will get selected if its part of the voxel system)
        //Set the properties too so they dont come undone on reload
        atlasMaterial.DisableKeyword("EXPLICIT_MAPS_ON");
        atlasMaterial.SetFloat("EXPLICIT_MAPS", 0);

        atlasMaterial.DisableKeyword("SLIDER_OVERRIDE_ON");
        atlasMaterial.SetFloat("SLIDER_OVERRIDE", 0);
        
        atlasMaterial.EnableKeyword("POINT_FILTER_ON");
        atlasMaterial.SetFloat("POINT_FILTER", 1);

        atlasMaterial.EnableKeyword("EMISSIVE_ON");
        atlasMaterial.SetFloat("EMISSIVE", 1);


        materials["atlas"] = atlasMaterial;

        //Finalize uvs etc
        foreach (var blockRec in loadedBlocks)
        {

            if (blockRec.Value.sideTexturePath != "")
            {
                blockRec.Value.sideUvs = atlas.GetUVs(blockRec.Value.sideTexturePath);
                blockRec.Value.averageColor[0] = atlas.GetColor(blockRec.Value.sideTexturePath);
                blockRec.Value.averageColor[1] = blockRec.Value.averageColor[0];
                blockRec.Value.averageColor[2] = blockRec.Value.averageColor[0];
            }
            else
            {
                blockRec.Value.sideUvs = new Rect(0, 0, 0, 0);
            }

            if (blockRec.Value.topTexturePath != "")
            {
                blockRec.Value.topUvs = atlas.GetUVs(blockRec.Value.topTexturePath);
                blockRec.Value.averageColor[1] = atlas.GetColor(blockRec.Value.topTexturePath);
                //Debug.Log("TopColor: " + blockRec.Value.name + " Color: " + blockRec.Value.averageColor[1]);
            }
            else
            {
                blockRec.Value.topUvs = blockRec.Value.sideUvs;
            }

            if (blockRec.Value.bottomTexturePath != "")
            {
                blockRec.Value.bottomUvs = atlas.GetUVs(blockRec.Value.bottomTexturePath);
            }
            else
            {
                blockRec.Value.bottomUvs = blockRec.Value.topUvs;
            }

            if (blockRec.Value.meshTexturePath != "")
            {
                blockRec.Value.mesh.AdjustUVs(atlas.GetUVs(blockRec.Value.meshTexturePath));

            }
        }
        Profiler.EndSample();
    }

    //Fix a voxel value up with its solid mask bit
    public VoxelData AddSolidMaskToVoxelValue(VoxelData voxelValue)
    {
        BlockDefinition block = GetBlock(VoxelWorld.VoxelDataToBlockId(voxelValue));

        if (block == null)
        {
            return voxelValue;
        }
        //Set bit 0x8000 based on wether block.solid is true
        voxelValue = (VoxelData)((VoxelData)voxelValue | (VoxelData)(block.solid ? 0x8000 : 0));
         
        return voxelValue;
    }

    private string ResolveAssetPath(string path)
    {
        if (m_bundlePaths == null)
        {
            string[] gameRootPaths = AssetBridge.GetAllGameRootPaths();
            
            string rootPath = Application.dataPath;
            string assetsFolder = "/Assets";
            if (rootPath.EndsWith(assetsFolder))
            {
                rootPath = rootPath.Substring(0, rootPath.Length - assetsFolder.Length);
            }

            m_bundlePaths = new() {Path.Combine(rootPath, "Bundles")};
            
            foreach (string gameRoot in gameRootPaths)
            {
                m_bundlePaths.Add(Path.Combine(rootPath, "Bundles"));
            }
        }
       
        //check each one for our path
        foreach (var bundlePath in m_bundlePaths)
        {
            string checkPath = Path.Combine(bundlePath, path);
            if (File.Exists(checkPath)) 
            {
                return checkPath;
            }
        }

        return path;
    }

    private Texture2D LoadTextureInternal(bool loadTextureDirectlyFromDisk, string path)
    {
        if (loadTextureDirectlyFromDisk == false)
        {
            return AssetBridge.LoadAssetInternal<Texture2D>(path, false);
        }
        else
        {
            //Do a direct file read of this thing
            Debug.Log("resolving path " + path);
            string newPath = ResolveAssetPath(path);
            Texture2D tex = TextureLoaderUtil.TextureLoader.LoadTexture(newPath);

            if (tex == null)
            {
                return null;
            }

            //Convert the texture to Linear space
            Texture2D linearTex = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false, true);
            linearTex.SetPixels(tex.GetPixels());
            linearTex.Apply();

            return linearTex;
        }
    }
    private TexturePacker.TextureSet LoadTexture(bool loadTexturesDirectlyFromDisk, string path, float roughness, float metallic, float normalScale, float emissive, float brightness)
    {
        Texture2D texture = LoadTextureInternal(loadTexturesDirectlyFromDisk, path + ".png");
        Texture2D texture_n = LoadTextureInternal(loadTexturesDirectlyFromDisk, path + "_n.png");
        Texture2D texture_r = LoadTextureInternal(loadTexturesDirectlyFromDisk, path + "_r.png");
        Texture2D texture_m = LoadTextureInternal(loadTexturesDirectlyFromDisk, path + "_m.png");
        Texture2D texture_e = LoadTextureInternal(loadTexturesDirectlyFromDisk, path + "_e.png");

        if (texture == null)
        {
            Debug.LogError("Failed to load texture: " + path);
            return null;
        }

        TexturePacker.TextureSet res = new(texture, texture_n, texture_r, texture_m, texture_e, roughness, metallic, normalScale, emissive, brightness);
        temporaryTextures.Add(path, res);
        return res;
    }

}
