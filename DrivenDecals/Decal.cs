using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace SamDriver.Decal {
  [ExecuteInEditMode]
  [RequireComponent(typeof(MeshRenderer))]
  [RequireComponent(typeof(MeshFilter))]
  /// <summary>
  /// Represents a single Decal instance in the scene.
  /// </summary>
  public class Decal : MonoBehaviour
  {
    public DecalAsset DecalAsset;
    public float Opacity = 1f;
    public float ZFadeDistance = 0.1f;
    public bool IsFlipU = false;
    public bool IsFlipV = false;
    public bool ShouldUseSceneStaticMeshes = true;
    public List<MeshFilter> MeshesToProjectAgainst = new List<MeshFilter>();

    [SerializeField] bool isMeshUnprojected = false;

    MeshFilter _meshFilter;
    MeshFilter meshFilter
    {
      get
      {
        if (_meshFilter == null) _meshFilter = GetComponent<MeshFilter>();
        return _meshFilter;
      }
    }
    MeshRenderer _meshRenderer;
    MeshRenderer meshRenderer
    {
      get
      {
        if (_meshRenderer == null) _meshRenderer = GetComponent<MeshRenderer>();
        return _meshRenderer;
      }
    }
    
    MaterialPropertyBlock materialPropertyBlock;

    static int boundsID = Shader.PropertyToID("_Bounds");
    static int opacityID = Shader.PropertyToID("_Opacity");
    static int zFadeStartID = Shader.PropertyToID("_ZFadeStart");
    static int flipUID = Shader.PropertyToID("_FlipU");
    static int flipVID = Shader.PropertyToID("_FlipV");

    public bool IsGeneratedMeshEmpty
    {
      get => (meshFilter.sharedMesh == null || meshFilter.sharedMesh?.vertexCount == 0);
    }
    
    /// <summary>
    /// Is it currently possible to ScaleToMatchDecalBoundsRatio for this decal.
    /// </summary>
    public bool CanScaleToMatchDecal
    {
      get => (DecalAsset != null) && !DecalAsset.HasAnyZeroSizedDimensions;
    }

    /// <summary>
    /// Is a DecalAsset currently selected.
    /// </summary>
    public bool HasDecalAsset { get => (DecalAsset != null); }

    /// <summary>
    /// Queries .HasAnyZeroSizedDimensions on the selected DecalAsset.
    /// False if there is no DecalAsset currently selected.
    /// </summary>
    public bool HasZeroDimensionsOnDecalAsset
    {
      get => (DecalAsset != null) && DecalAsset.HasAnyZeroSizedDimensions;
    }

    public bool HasMeshToProjectAgainst
    {
      get => WorldMeshFilters().Any();
    }

    void OnEnable()
    {
      materialPropertyBlock = new MaterialPropertyBlock();
      SetupMaterialPropertyBlock();
    }

    void EnforcePositiveScale()
    {
      if (transform.localScale.x < 0f)
      {
        transform.localScale = new Vector3(
          transform.localScale.x * -1f, 
          transform.localScale.y, 
          transform.localScale.z
        );
      }
      if (transform.localScale.y < 0f)
      {
        transform.localScale = new Vector3(
          transform.localScale.x, 
          transform.localScale.y * -1f, 
          transform.localScale.z
        );        
      }
      if (transform.localScale.z < 0f)
      {
        transform.localScale = new Vector3(
          transform.localScale.x, 
          transform.localScale.y, 
          transform.localScale.z * -1f
        );        
      }
    }
    
    // assume DecalAsset will not change during runtime, so only automatically update when in editor
    // If DecalAsset does change during runtime you should call SetupMaterialPropertyBlock()
    #if UNITY_EDITOR
    void Update()
    {
      SetupMaterialPropertyBlock();
    }
    #endif

    /// <summary>
    /// Sets appropriate Material on object's MeshRenderer and uses a MaterialPropertyBlock
    /// to provide appropriate values for rendering this decal.
    /// Should be called whenever there's a change to DecalAsset or this Decal's properties.
    /// </summary>
    public void SetupMaterialPropertyBlock()
    {
      if (DecalAsset == null) return;

      meshRenderer.sharedMaterial = DecalAsset.Material;
      meshRenderer.GetPropertyBlock(materialPropertyBlock);

      materialPropertyBlock.SetVector(boundsID, DecalAsset.BoundsAsVector4);
      materialPropertyBlock.SetFloat(opacityID, Opacity);
      materialPropertyBlock.SetFloat(zFadeStartID, isMeshUnprojected ? 0f : ZFadeDistance);
      materialPropertyBlock.SetInt(flipUID, IsFlipU ? 1 : 0);
      materialPropertyBlock.SetInt(flipVID, IsFlipV ? 1 : 0);

      meshRenderer.SetPropertyBlock(materialPropertyBlock);
    }

    /// <summary>
    /// Adjust this object's local scale to make its aspect ratio match selected decal texture's bounds.
    /// Will decrease either the x or y component of local scale, never changes z.
    /// 
    /// Will throw DecalException if unable to perform the scale operation.
    /// You should check CanScaleToMatchDecal before calling.
    /// </summary>
    public void ScaleToMatchDecalBoundsRatio()
    {
      if (DecalAsset == null)
      {
        throw new DecalException($"{nameof(SamDriver.Decal.Decal)} requires a {nameof(SamDriver.Decal.DecalAsset)} in order to match its scale.");
      }
      else if (DecalAsset.HasAnyZeroSizedDimensions)
      {
        throw new DecalException($"{nameof(SamDriver.Decal.Decal)} cannot scale to match a {nameof(SamDriver.Decal.DecalAsset)} with zero width or height.");
      }

      #if UNITY_EDITOR
      UnityEditor.Undo.RecordObjects(new UnityEngine.Object[] { this, this.transform }, "rescale decal");
      #endif 

      EnforcePositiveScale();

      Vector3 scale = transform.localScale;
      float targetRatio = DecalAsset.TexelsWidth / DecalAsset.TexelsHeight;
      float currentRatio = scale.x / scale.y;

      if (currentRatio > targetRatio)
      {
        // current is too wide, so reduce width
        scale.x = targetRatio * scale.y;
      }
      else
      {
        // current is too tall, so reduce height
        scale.y = scale.x / targetRatio;
      }

      transform.localScale = scale;

      #if UNITY_EDITOR
      // if we're part of a prefab need to report change through PrefabUtility for undo etc to work correctly
      if (UnityEditor.PrefabUtility.IsPartOfAnyPrefab(this))
      {
        UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(this);
      }
      #endif
    }

    /// <summary>
    /// Create a simple quad mesh positioned on the "back" face of the decal's region.
    /// Intended for use as an initial display of where the decal is.
    /// Note that this overwrites any existing mesh, so will cause the loss of any
    /// projected mesh from GenerateProjectedMesh.
    /// </summary>
    public void GenerateSimpleQuadMesh()
    {
      #if UNITY_EDITOR
      UnityEditor.Undo.RecordObjects(
        new UnityEngine.Object[] {this, meshFilter}, "reset decal mesh"
      );
      #endif

      EnforcePositiveScale();

      List<Vector3> positions = new List<Vector3>();
      List<Vector2> uvs = new List<Vector2>();
      List<Vector3> normals = new List<Vector3>();
      List<Vector4> tangents = new List<Vector4>();
      foreach (Vector2 corner in Square.CenteredUnitSquareCorners)
      {
        positions.Add(new Vector3(corner.x, corner.y, -0.5f));
        uvs.Add(new Vector2(corner.x + 0.5f, corner.y + 0.5f));
        normals.Add(Vector3.back);
        tangents.Add(new Vector4(1f, 0f, 0f, -1f));
      }

      int[] indices = new int[] {
        0, 2, 1,
        0, 3, 2
      };
      
      var mesh = new Mesh();
      mesh.vertices = positions.ToArray();
      mesh.normals = normals.ToArray();
      mesh.tangents = tangents.ToArray();
      mesh.uv = uvs.ToArray();
      mesh.triangles = indices;

      meshFilter.mesh = mesh;
      isMeshUnprojected = true;

      #if UNITY_EDITOR
      // if we're part of a prefab need to report change through PrefabUtility for undo etc to work correctly
      if (UnityEditor.PrefabUtility.IsPartOfAnyPrefab(this))
      {
        UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(this);
        UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(meshFilter);
      }
      #endif
    }

    /// <summary>
    /// Create a mesh that represents the decal having been projected against the target mesh(es).
    /// The created mesh is set as this object's MeshFilter's current mesh.
    /// </summary>
    public void GenerateProjectedMesh()
    {
      if (!HasMeshToProjectAgainst)
      {
        throw new DecalException($"Projecting a {nameof(Decal)} requires at least one target mesh to project against.");
      }

      #if UNITY_EDITOR
      UnityEditor.Undo.RecordObjects(
        new UnityEngine.Object[] {this, meshFilter}, "project decal mesh");
      #endif

      EnforcePositiveScale();

      meshFilter.mesh = MeshProjection.GenerateProjectedDecalMesh(WorldMeshFilters(), this.transform);
      isMeshUnprojected = false;

      #if UNITY_EDITOR
      // if we're part of a prefab need to report change through PrefabUtility for undo etc to work correctly
      if (UnityEditor.PrefabUtility.IsPartOfAnyPrefab(this))
      {
        UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(this);
        UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(meshFilter);
      }
      #endif
    }

    /// <summary>
    /// Provides the MeshFilters that this decal should be projected against.
    /// How they are sourced is decided by ShouldUseAllSceneStaticMeshes.
    /// </summary>
    IEnumerable<MeshFilter> WorldMeshFilters()
    {
      if (ShouldUseSceneStaticMeshes)
      {
        // we'll (fairly roughly) construct a sphere that encompasses the whole of
        // the mesh in question, then see if that intersects a sphere around the decal.
        // a k-d tree or some other spatial mapping would be a good idea, but so long
        // as we're generating the mesh outside of runtime, it's not a priority.
        Vector3 decalCenter = this.transform.position;

        // decal is a unit cube, centered on decalCenter
        float decalRadiusSquared = (this.transform.TransformPoint(Vector3.one * 0.5f) - decalCenter).sqrMagnitude;

        foreach (MeshFilter meshFilter in GameObject.FindObjectsOfType(typeof(MeshFilter)))
        {
          // only interested in meshes on static objects
          if (!meshFilter.gameObject.isStatic) continue;

          // don't project against other decals
          if (meshFilter.gameObject.TryGetComponent<Decal>(out _)) continue;

          //NOTE: if you want to exclude other objects from "all static objects" here's the place to do it

          var bounds = meshFilter.sharedMesh.bounds;
          Vector3 meshCenter = meshFilter.transform.TransformPoint(bounds.center);
          float meshRadiusSquared = (meshFilter.transform.TransformPoint(bounds.max) - meshCenter).sqrMagnitude;

          float separationSquared = (meshCenter - decalCenter).sqrMagnitude;
          if (separationSquared < (decalRadiusSquared + meshRadiusSquared))
          {
            yield return meshFilter;
          }
        }
      }
      else
      {
        foreach (MeshFilter meshFilter in MeshesToProjectAgainst)
        {
          yield return meshFilter;
        }
      }
    }
    
    #if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
      DrawGizmoDecalRegion();
    }

    void DrawGizmoDecalRegion()
    {
      Gizmos.color = Color.grey;
      for (int i = 0; i < Square.CornerCount; ++i)
      {
        Vector2 v0 = Square.CenteredUnitSquareCorners[i];
        Vector2 v1 = Square.CenteredUnitSquareCorners[(i + 1) % Square.CornerCount];

        // back
        Gizmos.DrawLine(
          transform.TransformPoint(new Vector3(v0.x, v0.y, -0.5f)),
          transform.TransformPoint(new Vector3(v1.x, v1.y, -0.5f))
        );
        // connecting
        Gizmos.DrawLine(
          transform.TransformPoint(new Vector3(v0.x, v0.y, -0.5f)),
          transform.TransformPoint(new Vector3(v0.x, v0.y, +0.5f))
        );
        // front
        Gizmos.DrawLine(
          transform.TransformPoint(new Vector3(v0.x, v0.y, +0.5f)),
          transform.TransformPoint(new Vector3(v1.x, v1.y, +0.5f))
        );
      }
    }
    #endif //UNITY_EDITOR
  }
}
