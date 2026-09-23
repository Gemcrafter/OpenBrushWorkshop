// Standard shader with triplanar mapping
// https://github.com/keijiro/StandardTriplanar

Shader "Standard Triplanar"
{
    Properties
    {
        _Color("", Color) = (1, 1, 1, 1)
        _MainTex("", 2D) = "white" {}

        _Glossiness("", Range(0, 1)) = 0.5
        [Gamma] _Metallic("", Range(0, 1)) = 0

        _BumpScale("", Float) = 1
        _BumpMap("", 2D) = "bump" {}

        _OcclusionStrength("", Range(0, 1)) = 1
        _OcclusionMap("", 2D) = "white" {}

        _MapScale("", Float) = 1
    }
        SubShader
        {
         Cull Back
        Tags { "RenderType"="Opaque" } //Backculling differs from transparent

        CGPROGRAM

        #pragma surface surf Standard vertex:vert fullforwardshadows addshadow //alpha channel not added

        #pragma shader_feature _NORMALMAP
        #pragma shader_feature _OCCLUSIONMAP

        #pragma target 3.0

        half4 _Color;
        sampler2D _MainTex;

        half _Glossiness;
        half _Metallic;

        half _BumpScale;
        sampler2D _BumpMap;

        half _OcclusionStrength;
        sampler2D _OcclusionMap;

        half _MapScale;
       
        struct Input
        {
            float3 localCoord;
            float3 localNormal;
        };

        void vert(inout appdata_full v, out Input data)
        {
            UNITY_INITIALIZE_OUTPUT(Input, data);
            data.localCoord = v.vertex.xyz;
            data.localNormal = v.normal.xyz;
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            // Blending factor of triplanar mapping
            float3 bf = normalize(abs(IN.localNormal));
            bf /= dot(bf, (float3)1);

            // Triplanar mapping
            float2 tx = IN.localCoord.yz * _MapScale;
            float2 ty = IN.localCoord.zx * _MapScale;
            float2 tz = IN.localCoord.xy * _MapScale;

            // Base color
            half4 cx = tex2D(_MainTex, tx) * bf.x;
            half4 cy = tex2D(_MainTex, ty) * bf.y;
            half4 cz = tex2D(_MainTex, tz) * bf.z;
            half4 color = (cx + cy + cz) * _Color;
            o.Albedo = color.rgb;
            o.Alpha = color.a;

        #ifdef _NORMALMAP
            // Normal map
            half4 nx = tex2D(_BumpMap, tx) * bf.x;
            half4 ny = tex2D(_BumpMap, ty) * bf.y;
            half4 nz = tex2D(_BumpMap, tz) * bf.z;
            o.Normal = UnpackScaleNormal(nx + ny + nz, _BumpScale);
        #endif

        #ifdef _OCCLUSIONMAP
            // Occlusion map
            half ox = tex2D(_OcclusionMap, tx).g * bf.x;  //what is .g?
            half oy = tex2D(_OcclusionMap, ty).g * bf.y;
            half oz = tex2D(_OcclusionMap, tz).g * bf.z;
            o.Occlusion = lerp((half4)1, ox + oy + oz, _OcclusionStrength);
        #endif

            // Misc parameters
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
        }
        ENDCG
    }
    FallBack "Diffuse"
    CustomEditor "StandardTriplanarInspector"
}

// StandardTriplanar.shader vs. StandardTriplanarTransparent.shader
// lines 25, 29 are only differences (alpha to pragma, and backcull render type) between triplanar and transparent
// the various map patterns are same pattern of modified map coordinates (.g active on occlusion map)
// misc params are applied generally from the material rather than on a vertex basis
// presumably could add sampler2d (39-40) to sample these properties, at higher cost?

//RE Square3DPrintBrush.cs
// (not connected, but relevant)
// Bevel angle can be controlled
// several params that could be exposed for more control of shape (diamond to rectange slope, number of sides.. or copied and adjusted)
// ideally find a way to apply color/bump etc mapping from here to square 3d print brush variants

//Alpha values are single colors (range) [57] with m_transparency set at value 255 during tesselation [55]
// [119] self refrences by script name (change if using modified script)
//[148] PressuredSize is a factor in this brush knot construction
// [259] sets color alpha transparency as making geometry], may be making unused areas invisible/transparent
// most of the script is knot handling

// [547] determines if continues to pivot or reaches endpont
//[575] Pulls vertex layout from TubeBrush.cs and excludes UVs and tangents from that input (preserves transparent sections?)
//simplifies AppendVertProcess and introduces color information... May be possible to inject more data here
// AppendVert vs AppendVertSquare (in square3d)
//[596] appears to be adding color here instead of geom pool
// ??? maybe assign a random color at this point from a range instead of a direct c value in the Color32 selection?)

//[576] to [604] handles the monochrome by simplifying the color system to a single color, and discardes normals and tangent maps in favor of simplified color
// UVsize for channels 0,1 are set to zero

// compare TubeBrush.cs
// Also note that this brush appears to use pressure
// Topology note and comments also in this file
                        // Start and end verts wrap differently between soft / hard edge geometry due to our topology
                        // TO DO: Refactor this so that things... are sane.  Not sure exactly how to do that elegantly though


// Base Brush Script [118-704] can be opened to see accessor values, helpers, and overrides
// ParentBrush.cs [460] GetNumUsedVerts()  .. checks subbrushes for verts? possible to iterate over for per vertex changes?

//Quaternion (quaternions are used to express rotations)

//TubeBrush.cs
// TubeBrush : GeometryBrush
// Tube brush tracks per vert displacement directions for use with the vert post processing modifier
// [710] to [740] Brush cases where curves are set (m_ShapeModifier) can set sin and Scalar tapering there, based on (t)= time interval of stroke?
//[744-748] (MakeCapVerts) (sets the end cap vertex positions and edges based on curved or soft, 
// explore transparency near verts or different methods of closing
//[781] (MakeClosedCircle) distinguishes between circle hard and soft edges
// [796] (MakeClosedCircleSoftEdges) verts=points+1 extra vertex is used for wrapping UVs
// [822] (MakeClosedCircleHardEdges) verts=points*2, vertex order go clockwise to keep seam at bottom
// ?? Seam handling might be interesting to play with
// Edge types are not reconciled between hard and soft (todo)
// [445] Correct UVS on end caps is listed as (todo)
 
// [614]Post process vert geometry per segment.
// Can be leveraged for more interesting shapes
//void OnChanged_ModifySilhouette(int iChangedKnot)

//--------------------
// BaseBrushScript.cs
// Useful variables, functions, definitions

//[61-123] Loads prfebs, loads brush weights, initializes brushes, calculates stroke costs, handles scaling for previews
// LOCAL_TO_POINTER and m_LastSpawnXf.scale and mBaseSize_Ps and mBaseSize_Ls 
// appear to control brush size to canvas ratios and room coordinate system relative to each other

//[123-164] accessors, 
// canvas uses (_LastSpawnXf.scale)
// to handle scaling based on current size when drawing rather than world scale
// assumes scale is unchanged since previous rescale. So brush size is proportionate to how zoomed out the player has the world (dinosaur, etc)
// This will also affect texture scaling and is probably why mapScale (or similar) returns to grids on large triplanars


// [169]
//public Color CurrentColor
// {   get { return m_Color; }  }


// [221] Interesting temporary brush data and material info for that purpose

        // A subset of InitBrush() that modifies only those things that can be
        // safely changed after the brush has been created.
        // Only used by preview brushes.
/*
public void SetPreviewProperties(Color rColor, float fSize)
{
    m_Color = rColor;
    m_BaseSize_PS = fSize;
    // TODO: do preview brushes really need this?
    GetComponent<Renderer>().material = m_Desc.Material;
}
*/


// [266]
        /// Returns the mesh vertex layout for this brush.
        /// The value returned may not change per brush descriptor.
        /// Non-geometric types will return a layout indicating no data requirements.
// public abstract GeometryPool.VertexLayout GetVertexLayout(BrushDescriptor desc);

/// Take any stored-up changes (to geometry, particles, etc) and update
/// a Mesh or other graphics resource. Guaranteed to be called on the
/// render thread.
/// 
// public abstract void ApplyChangesToVisuals();

//[286] to [316]
//GetNumUSedVerts() and other functions used to rebuild prveiew brushes, particle spawns and decay, and cloning if preview completes
//Knots to particle and random generation 
// See GeniusParticlesBrush for one possible solution.

//[317] to [352] position updates, line ends, discards and geometry debugging

// ---------------

// HELPERS FOR SUBCLASSES [354]
//[357] protected float PressuredSize(float pressure01)
// used to return brush size with canvas and pressure factored in
// is also a random function available to vary this
//[370] Opacity pressure controls

// HELPERS FOR SUBCLASSES  (static)
// [381] to [504] Mostly related to mirror handling and vertex sequencing
//[506] to [704] Computation of tangent space for quads, vector calculations, Uv positioning for entire quad chains


// -----------------------------------------------------
// Bloom.shader


//SquareBrush.cs
// [283]
// this function sets geometry and texture colors
/*
        void MySetVert(int iVert, int vp, Vector3 v, Vector3 n)
        {
            int i = iVert + vp * NS;
            m_geometry.m_Vertices[i] = v;
            m_geometry.m_Normals[i] = n;
            Color32 c = m_Color;
            c.a = 255;
            m_geometry.m_Colors[i] = c;
            m_geometry.m_Texcoord0.v2[i] = new Vector2(.5f, .5f);
        }
    
 */


// ---------------------
// TetraBrush.cs
// class TetraBrush : GeometryBrush
// appears to attempt meters to units calculations
// and make tubes, some interesting ovreloads and possibly color to vertex mapping




// ----------- ****************************** --------------
// UserVariantBrush.cs
// This handles the creation of user brushes
// [217] (comment) <returns>A created UserVariantBrush, or null if it couldn't be created.</returns>
// this function creates teh folder and creates the config file (possible error point)
//?? [233] interesting, appears to force into gui here
// if (brush.Initialize(brushFile, forceInGui: true)) returns brush, otherwise gives a null result
// [247] creates ssceefile and attempts to initialize the brush from the (kConfigFile) and initializes the brush file

    /// <param name="brushFile">The FolderOrZipReader used to read the brush.</param>
    /// <param name="forceInGui">Whether to force inclusion of the brush into the brushes panel.</param>
    /// <returns>Success or failure.</returns>
// HERE somewhere in here it is failign to create and returning a null value, possibly related to unreadable/writeable textures
//[284] handles the brush config read (has error catch for json read)
// [304] should log if a string input is empty/null
// SUSPECT MY ERRORS INVOLVED FAILING TO READ OR WRITE THE ACTUAL TEXTURE MAP GRAPHIC
// DOESNT SEEM TO BE AN ERROR CATCH THERE (READ/WRITE)
// debug command similar to this in 306 if section
//    Debug.Log($"Could not load brush at {m_Location}\n{warning}");
//[339 icon texture debug catch]
// [432]                     Debug.LogError($"Material does not have property ${item.Key}.");
// is this where it's checking?



// Texture2D Loading texture
// [492] Tries to load texture, would a read failure return null?
// HERE [511] function
//   private Texture2D LoadTexture(FolderOrZipReader brushFile, string filename)
//[511-529] Attempting to load texture data, and read the stream
// The read stream on 519, does not have an error catch.. probably failing to load into array
//[611]  
// Copy textures and update texture paths
// brush.UserVariantBrush.SaveorCopyTextures(brush.Material.shader, textureRefs);
// is copyiing texture references

//[842]to[911] 
// This grabs textures from unity and saves them internally
// copies path
// copies texture

// [334] Controls copy restriction on embed and share or do not embed
// Author of the brush is also set here

// *******************************************************************

// GEOMETRY POOL

//58 There is a required order to identify whether the last entry is a normal, vector, etc
// Would it make sense to assign a numeric key to that and use that for feedback, adding an identifier? 
// Presumably this would require significant refactoring since all the entries load into that individually?
//[155]          public struct VertexLayout : IEquatable<VertexLayout>
// booleans (bUseColors)(bUseTangents)(bUseNormals, and texcoord[0 1 and 2)
// also handles normal flips

// Has things like static functions for GetVertexCount, Mesh reloads, reloads FromFile, backing files, 
// [284] take ownership from list (applicable to read/write?)
// Geometry construction/destruction and other management is in this file
// handling right rotations/facings and transforms

// [1223] comments
// Apply a transform to a subset of the geometry.
        ///
        /// Pass:
        ///   xf -
        ///     Transform to apply to positions, vectors.
        ///
        ///   xfBivector -
        ///     Transform to apply to normals (unless they are Position or Vector semantic) and tangents.
        ///     There are two ways in which normals and tangents are not like positions, vectors:
        ///
        ///     1. They are bivectors -- cross products. They must honor this transform rule:
        ///          xf * (a x b) === (xf * a) x (xf * b)
        ///        Since each of (xf * a and (xf * b) get some scale, cross products get
        ///        the scale factored in _twice_.
        ///        See TrTransform.MultiplyBivector and its documentation for more info.
        ///
        ///     2. Unrelated to being bivectors, they are almost always constrained to be unit-length.
        ///        Typically this is implemented by constraining their scaling to 1 or -1.
        ///
        ///     If you put rules 1 and 2 together, you get (scale * scale) / abs(scale * scale)
        ///     which is always 1. Since normals don't get _translated_ either, typically just
        ///     pass xf.rotation.
        ///
        ///     EXCEPTION: you can add in some extra -1 scale if you need to flip your normals
        ///     in order to complete some winding change; see ExportUtils.cs.
        ///
        ///   xfDist -
        ///     Transform to apply to distance scalars. This should probably be Abs(xf.uniformScale),
        ///     but you have to pass it explicitly since nobody wants to extract scale from a mat4.
        /// 

// continues with various transforms and vectorinformation
// specifically with approaches to subsets and partial transforms 

// [1478] to [1519] Validation of stream contents


//GeometryBrush.cs
// Contains struct for Knot, along with much documentation


// [starts at 505, 521 in particularSUSPECT IO CODE ON TEXTURE WRITE, BUILDS ARRAY
// byte[] data = buffer.ToArray();
/*
    /// <summary>
    /// Loads a texture from a FolderOrZipReader.
    /// </summary>
    /// <param name="brushFile">FolderOrZipReader to load the texture from.</param>
    /// <param name="filename">Texture filename.</param>
    /// <returns>A Texture2D, or null if it could not be loaded.</returns>
private Texture2D LoadTexture(FolderOrZipReader brushFile, string filename)
{
    if (brushFile.Exists(filename))
    {
        Texture2D texture = new Texture2D(16, 16);
        var buffer = new MemoryStream();
        using (var bufferStream = brushFile.GetReadStream(filename))
        {
            bufferStream.CopyTo(buffer);
        }
        byte[] data = buffer.ToArray();
        m_FileData[filename] = data;
        if (ImageConversion.LoadImage(texture, data, true))
        {
            texture.name = Path.GetFileNameWithoutExtension(filename);
            return texture;
        }
    }
    return null;
}

/// <summary>
/// Saves the UserVariantBrush.
/// </summary>
/// <param name="writer">AtomicWriter to write to.</param>
/// <param name="subfolder">Subfolder within the writer to write to.</param>
public void Save(AtomicWriter writer, string subfolder)
{
    string configPath = Path.Combine(subfolder, Path.Combine(m_Location, kConfigFile));
    using (var configStream = new StreamWriter(writer.GetWriteStream(configPath)))
    {
        configStream.Write(m_ConfigData);
    }

    foreach(var item in m_FileData)
    {
        string path = Path.Combine(subfolder, Path.Combine(m_Location, item.Key));
        using (var dataWriter = writer.GetWriteStream(path))
        {
            dataWriter.Write(item.Value, 0, item.Value.Length);
        }
    }
}
*/



// *********************************************************************************
// ---------------------------
// THINGS TO EXPERIMENT WITH, VARIANTS
// ---------------------------
// Simple Todos, variant brushes to hex and 8 sided, (want a 16 or 32 face brush for tunneling)
// protected Enums for shape modifiers are in TubeBrush.cs
//    class TubeBrush : GeometryBrush
// Look at specific Geometry brushes for inputs like number of vertexes around the circle (inscribed shape)
// [34] Points in closed circle, unclear how m_CapAspect works, search

// TubeBrush input settings in unity, may be able to set pressure to create bulbs, variant radiuses
// TEST Make a test brush with texture icons set to non-readable, see if triggers an error and error message
// UserVariantBrush.cs 
// [289 possible need for errror catch on write stream]

// CLASSES
// noticed these in different Geometry brush , do they indicate parenting?
// class FlatGeometryBrush : GeometryBrush
//     public class ConcaveHullBrush : GeometryBrush
//     public abstract class GeometryBrush : BaseBrushScript

// see GeometryPool [1478] to [1519] Validation of stream contents

// Create shortcut list at end with line numbers for various topics

// **************************************************************************************










