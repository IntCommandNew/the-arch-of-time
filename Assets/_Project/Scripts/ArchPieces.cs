using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>What can be collected: a piece of the arch, or a wax seal (it opens a secret).</summary>
public enum ItemKind
{
    Piece,
    Seal,
}

/// <summary>
/// The arch pieces and the seals. A piece is the arch model's own piece (Models/Arch/arch.fbx: LeftColumn,
/// RightColumn, Arc, TopBeam = pieces 0 to 3), scaled so its longest side is pieceLength and turning around
/// its own middle; a seal is the wax seal model (Models/Seal/seal.fbx, its stamped face +Z towards the user),
/// sized the same way. (Primitives stand in only if a model is missing.)
///
/// Spawn(kind, index): the item appears at a random spot 1.5 to 4 m around the user, in any direction (also
/// behind), floating ~1 m above the detected floor and anchored in AR so it stays put. It slowly turns and
/// bobs. The centre of the screen is the aim: when it is on the item within reach, it lights up (IsAimed) and
/// Collect() takes it (it spins away). One item is out at a time. A collected piece is saved per mode
/// (GameProgress); a collected seal only raises ItemCollected (QuestFlow saves it with its secret).
///
/// In-city mode: ShowOn(anchor, piece) puts that particular piece on a geospatial anchor (CityContent),
/// bigger and collectable from farther away, since it is found outdoors at a GPS-level place.
///
/// No UI of its own: AppController and QuestFlow show the count, the hint and the buttons.
/// </summary>
public class ArchPieces : MonoBehaviour
{
    [Header("AR (XR Origin and Main Camera)")]
    [SerializeField] ARRaycastManager raycastManager;
    [SerializeField] ARAnchorManager anchorManager;
    [SerializeField] ARPlaneManager planeManager;
    [SerializeField] Camera arCamera;

    [Header("Piece")]
    [Tooltip("Models/Arch/arch.fbx: its children LeftColumn, RightColumn, Arc and TopBeam are the pieces.")]
    [SerializeField] GameObject archPieceModels;
    [Tooltip("Longest side of a piece around the user (metres): as big as the old placeholder block.")]
    [SerializeField] float pieceLength = 0.3f;
    [Tooltip("The academy's stone material (m_stone): the base of the placeholder seal's wax material.")]
    [SerializeField] Material pieceMaterial;
    [Tooltip("Multiplies the item's colour at the top of the aimed pulse: a slightly warm brightening, so the " +
             "stone keeps its own colours (a strong gold, 2.2 / 1.7 / 0.7, made every piece look the same).")]
    [SerializeField] Color aimedTint = new Color(1.5f, 1.42f, 1.22f);
    [Tooltip("While aimed at, the item's light pulses this many times a second (user, 21 Nov 2026: the old strong " +
             "gold tint hid what the piece was).")]
    [SerializeField] float aimedPulsesPerSecond = 1.5f;
    [SerializeField] float turnDegreesPerSecond = 30f;
    [SerializeField] float bobHeight = 0.05f;

    [Header("Seal")]
    [Tooltip("Models/Seal/seal.fbx: the academy's wax seal (made with Claude web, 21 Nov 2026).")]
    [SerializeField] GameObject sealModel;
    [Tooltip("The seal faces the user and sways this many degrees to each side (its back is plain wax).")]
    [SerializeField] float sealSwayDegrees = 20f;
    [Tooltip("Diameter of the seal (metres; its longest side), and the thickness of the fallback disc.")]
    [SerializeField] Vector2 sealSize = new Vector2(0.36f, 0.06f); // twice the first size: its stamp reads
    [SerializeField] Color sealColor = new Color(0.55f, 0.07f, 0.06f);

    [Header("Spawning")]
    [Tooltip("How far from the user an item appears (metres; user, 21 Nov 2026: closer, for small rooms).")]
    [SerializeField] float minDistance = 0.9f;
    [SerializeField] float maxDistance = 2.2f;
    [Tooltip("Height of the item above the floor (metres).")]
    [SerializeField] float heightAboveFloor = 1f;
    [Tooltip("Floor height below the camera when no floor has been detected (metres).")]
    [SerializeField] float fallbackFloorBelowCamera = 1.4f;

    [Header("In-city pieces (ShowOn)")]
    [Tooltip("Longest side of an in-city piece (metres), as big as the blocks previewed in the authoring scene.")]
    [SerializeField] float cityPieceLength = 1.2f;
    [SerializeField] float cityCollectDistance = 40f;
    [SerializeField] float cityAimTolerance = 0.9f;

    [Header("Collecting")]
    [Tooltip("Farthest an item can be collected from (metres).")]
    [SerializeField] float collectDistance = 6f;
    [Tooltip("How far (metres) the aim may pass from the item's centre and still count.")]
    [SerializeField] float aimTolerance = 0.25f;
    [SerializeField] float collectSeconds = 0.4f;
    [Tooltip("Seconds between Changed events while an item is out (keeps the direction hint current).")]
    [SerializeField] float refreshSeconds = 0.3f;

    /// <summary>Count, item, aim or direction changed.</summary>
    public event Action Changed;

    /// <summary>An item was collected (its spin finished): the kind and its piece / seal number.</summary>
    public event Action<ItemKind, int> ItemCollected;

    public GameMode Mode { get; private set; } = GameMode.Free;
    public int Collected => GameProgress.CollectedCount(Mode);
    public int Total => GameProgress.PieceCount;
    public bool AllCollected => GameProgress.NextPiece(Mode) < 0;

    /// <summary>An item is out and can be collected (not spinning away).</summary>
    public bool HasPiece => piece != null && !collecting;

    /// <summary>What is out (or being spawned): a piece or a seal.</summary>
    public ItemKind OutKind { get; private set; } = ItemKind.Piece;

    /// <summary>The piece / seal number of what is out, or -1.</summary>
    public int OutIndex => piece != null || IsSpawning ? shownPiece : -1;

    public bool IsAimed { get; private set; }
    public bool IsSpawning { get; private set; }
    /// <summary>The last piece collected (0 to 3), or -1.</summary>
    public int LastCollected { get; private set; } = -1;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();

    bool active;
    int spawnVersion;           // bumped by RemovePiece(), so a spawn still in progress is thrown away
    ARAnchor pieceAnchor;
    Transform pieceRoot;        // the anchor (in the Editor, a stand-in)
    Transform piece;            // the item that is out (for a seal: its holder)
    Renderer pieceRenderer;
    Material itemMaterial;      // the material of the item that is out (stone, or the seal's wax)
    Material sealMaterial;      // made once from the stone material: red wax, no texture
    MaterialPropertyBlock block;
    bool collecting;
    float collectStart;
    float nextRefresh;
    int shownPiece = -1;        // the piece / seal number of the item that is out
    Vector3 size;               // of the item that is out
    float baseHeight;           // its height above its anchor
    float reach;                // how far it can be collected from
    float tolerance;            // how far the aim may pass from it

    /// <summary>Items can be spawned and collected from now on, saved as progress of this mode.</summary>
    public void Begin(GameMode mode)
    {
        Mode = mode;
        active = true;
        LastCollected = -1;
        Changed?.Invoke();
    }

    /// <summary>No more items for now: the one that is out goes away (progress is kept).</summary>
    public void End()
    {
        active = false;
        RemovePiece();
    }

    /// <summary>Removes the item that is out, if any (e.g. a seal left behind when the quest moves on).</summary>
    public void Remove()
    {
        RemovePiece();
        Changed?.Invoke();
    }

    /// <summary>Starts this mode's quest again from 0.</summary>
    public void ResetProgress()
    {
        RemovePiece();
        GameProgress.ResetAll(Mode);
        Changed?.Invoke();
    }

    // Spawning
    /// <summary>Puts the next piece at a random spot around the user (replacing what is out).</summary>
    public void Spawn() => Spawn(ItemKind.Piece, GameProgress.NextPiece(Mode));

    /// <summary>Puts this piece or seal at a random spot around the user (replacing what is out).</summary>
    public async void Spawn(ItemKind kind, int index)
    {
        if (!active || IsSpawning || index < 0 || arCamera == null) return;
        RemovePiece();
        IsSpawning = true;
        int version = spawnVersion;
        OutKind = kind;
        shownPiece = index;
        size = kind == ItemKind.Seal ? new Vector3(sealSize.x, sealSize.y, sealSize.x) : Vector3.one * pieceLength;
        baseHeight = heightAboveFloor;
        reach = collectDistance;
        tolerance = aimTolerance;
        Changed?.Invoke();

        Vector3 cameraPosition = arCamera.transform.position;
        Vector3 direction = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f) * Vector3.forward;
        Vector3 spot = cameraPosition + direction * UnityEngine.Random.Range(minDistance, maxDistance);
        spot.y = FindFloor(spot, cameraPosition);
        var pose = new Pose(spot, Quaternion.LookRotation(-direction, Vector3.up)); // faces the user

        ARAnchor anchor = null;
        Transform root = null;
        try
        {
            if (anchorManager != null && anchorManager.enabled && anchorManager.subsystem != null)
            {
                var result = await anchorManager.TryAddAnchorAsync(pose);
                if (result.status.IsSuccess() && result.value != null)
                {
                    anchor = result.value;
                    root = anchor.transform;
                }
                else
                {
                    Debug.LogWarning($"[ArchPieces] The {kind}'s anchor could not be created ({result.status}).");
                }
            }
#if UNITY_EDITOR
            else
            {
                root = new GameObject("Item anchor (Editor stand-in)").transform;
                root.SetPositionAndRotation(pose.position, pose.rotation);
            }
#endif
        }
        catch (Exception e)
        {
            Debug.LogException(e, this);
        }

        if (version != spawnVersion || !active)
        {
            RemoveAnchor(anchor, root); // left the mode meanwhile
            return;
        }
        IsSpawning = false;
        if (root != null)
        {
            pieceAnchor = anchor;
            pieceRoot = root;
            piece = CreatePiece(root);
            Debug.Log($"[ArchPieces] {kind} {index} spawned {Vector3.Distance(cameraPosition, spot):0.0} m away.");
        }
        Changed?.Invoke();
    }

    /// Floor height under a spot: the detected floor right below it, else the lowest detected
    /// upward-facing plane, else a guess below the camera.
    float FindFloor(Vector3 spot, Vector3 cameraPosition)
    {
        if (raycastManager != null && raycastManager.subsystem != null &&
            raycastManager.Raycast(new Ray(new Vector3(spot.x, cameraPosition.y, spot.z), Vector3.down), hits,
                                   TrackableType.PlaneWithinPolygon))
        {
            foreach (var hit in hits)
                if (hit.trackable is ARPlane p && p.alignment == PlaneAlignment.HorizontalUp)
                    return hit.pose.position.y;
        }

        float lowest = float.PositiveInfinity;
        if (planeManager != null)
            foreach (var plane in planeManager.trackables)
                if (plane.alignment == PlaneAlignment.HorizontalUp && plane.center.y < cameraPosition.y - 0.3f)
                    lowest = Mathf.Min(lowest, plane.center.y);
        return float.IsPositiveInfinity(lowest) ? cameraPosition.y - fallbackFloorBelowCamera : lowest;
    }

    /// <summary>
    /// In-city mode: shows a particular piece on an anchor made elsewhere (its place in the city).
    /// This component then owns the anchor: End() removes it.
    /// </summary>
    public void ShowOn(Transform root, int pieceNumber)
    {
        if (!active || root == null) return;
        RemovePiece();
        OutKind = ItemKind.Piece;
        shownPiece = pieceNumber;
        size = Vector3.one * cityPieceLength;
        baseHeight = 0f; // the anchor is already at the piece's height
        reach = cityCollectDistance;
        tolerance = cityAimTolerance;
        pieceRoot = root;
        piece = CreatePiece(root);
        Debug.Log($"[ArchPieces] Piece {pieceNumber} shown on {root.name}.");
        Changed?.Invoke();
    }

    static readonly string[] PieceNames = { "LeftColumn", "RightColumn", "Arc", "TopBeam" };

    Transform CreatePiece(Transform root)
    {
        bool seal = OutKind == ItemKind.Seal;
        // A holder carries the motion (turning, bobbing, the collect spin); the shape inside carries the size.
        var holder = new GameObject(seal ? "Seal" : "Arch piece").transform;
        holder.SetParent(root, false);
        holder.localPosition = new Vector3(0f, baseHeight, 0f);
        var pieceModel = seal ? SealModel() : PieceModel(shownPiece);
        if (pieceModel != null)
        {
            // The arch's piece (its mesh is in the arch's space, pivot at the arch's foot) or the seal: moved to
            // turn around its own middle, and scaled so its longest side is the wanted length.
            var shape = Instantiate(pieceModel, holder, false);
            shape.name = seal ? "Seal model" : "Arch piece " + PieceNames[shownPiece];
            var bounds = shape.GetComponent<MeshFilter>().sharedMesh.bounds;
            float scale = Mathf.Max(size.x, size.y, size.z) /
                          Mathf.Max(0.001f, Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z));
            shape.transform.localRotation = Quaternion.identity;
            shape.transform.localScale = Vector3.one * scale;
            shape.transform.localPosition = -bounds.center * scale;
            pieceRenderer = shape.GetComponent<MeshRenderer>();
            itemMaterial = pieceRenderer.sharedMaterial;
        }
        else
        {
            // A model is missing: a primitive stands in (a red wax disc, a stone block).
            var shape = GameObject.CreatePrimitive(seal ? PrimitiveType.Cylinder : PrimitiveType.Cube);
            shape.name = seal ? "Seal (placeholder)" : "Arch piece (placeholder)";
            Destroy(shape.GetComponent<Collider>()); // aiming is a ray test, no physics
            shape.transform.SetParent(holder, false);
            if (seal)
            {
                // The disc faces the user; a cylinder primitive is 2 units tall, so half the thickness on Y.
                shape.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                shape.transform.localScale = new Vector3(size.x, size.y * 0.5f, size.z);
            }
            else
            {
                shape.transform.localScale = new Vector3(size.x, size.y * 0.66f, size.z * 0.5f);
            }
            pieceRenderer = shape.GetComponent<MeshRenderer>();
            itemMaterial = seal ? SealMaterial() : pieceMaterial;
            if (itemMaterial != null) pieceRenderer.sharedMaterial = itemMaterial;
        }
        block ??= new MaterialPropertyBlock();
        SetTint(Color.white);
        return holder;
    }

    /// The seal model (its mesh on the root), or null.
    GameObject SealModel() => sealModel != null && sealModel.GetComponent<MeshFilter>() != null ? sealModel : null;

    /// The arch model's piece (a child of arch.fbx), or null.
    GameObject PieceModel(int index)
    {
        if (archPieceModels == null || index < 0 || index >= PieceNames.Length) return null;
        var child = archPieceModels.transform.Find(PieceNames[index]);
        return child != null && child.GetComponent<MeshFilter>() != null ? child.gameObject : null;
    }

    Material SealMaterial()
    {
        if (sealMaterial != null || pieceMaterial == null) return sealMaterial;
        sealMaterial = new Material(pieceMaterial) { name = "Seal wax (placeholder)" };
        if (sealMaterial.HasProperty("_BaseMap")) sealMaterial.SetTexture("_BaseMap", null);
        if (sealMaterial.HasProperty(BaseColorId)) sealMaterial.SetColor(BaseColorId, sealColor);
        if (sealMaterial.HasProperty("_Smoothness")) sealMaterial.SetFloat("_Smoothness", 0.45f);
        return sealMaterial;
    }

    void OnDestroy()
    {
        if (sealMaterial != null) Destroy(sealMaterial);
    }

    // Aiming and collecting
    /// <summary>Collects the item being aimed at (it spins away; a piece adds to the count).</summary>
    public void Collect()
    {
        if (!HasPiece || !IsAimed) return;
        collecting = true;
        collectStart = Time.unscaledTime;
        IsAimed = false;
        Changed?.Invoke();
    }

    /// <summary>Where the item is from the user's view: angle (positive = right), distance, on screen.</summary>
    public bool TryGetPieceDirection(out float angle, out float distance, out bool onScreen)
    {
        angle = 0f; distance = 0f; onScreen = false;
        if (!HasPiece || arCamera == null) return false;
        var cameraTransform = arCamera.transform;
        Vector3 toPiece = piece.position - cameraTransform.position;
        distance = toPiece.magnitude;
        Vector3 forward = cameraTransform.forward; forward.y = 0f;
        Vector3 flat = toPiece; flat.y = 0f;
        angle = forward.sqrMagnitude > 1e-6f && flat.sqrMagnitude > 1e-6f
            ? Vector3.SignedAngle(forward, flat, Vector3.up)
            : 0f;
        Vector3 viewport = arCamera.WorldToViewportPoint(piece.position);
        onScreen = viewport.z > 0f &&
                   viewport.x > 0.05f && viewport.x < 0.95f &&
                   viewport.y > 0.05f && viewport.y < 0.95f;
        return true;
    }

    void Update()
    {
        if (piece == null) return;

        if (collecting)
        {
            float t = Mathf.Clamp01((Time.unscaledTime - collectStart) / collectSeconds);
            piece.localScale = Vector3.one * (1f - t);
            piece.Rotate(0f, 720f * Time.unscaledDeltaTime, 0f, Space.Self);
            piece.localPosition += Vector3.up * (0.6f * Time.unscaledDeltaTime);
            if (t >= 1f)
            {
                var kind = OutKind;
                int collected = shownPiece >= 0 ? shownPiece : GameProgress.NextPiece(Mode);
                RemovePiece();
                if (kind == ItemKind.Piece)
                {
                    GameProgress.MarkCollected(Mode, collected);
                    LastCollected = collected;
                }
                Debug.Log($"[ArchPieces] {kind} {collected} collected ({Mode} mode): {Collected} / {Total} pieces.");
                Changed?.Invoke();
                ItemCollected?.Invoke(kind, collected);
            }
            return;
        }

        // Idle animation: a piece turns slowly; a seal faces the user (its stamped face, +Z), swaying a little,
        // so its back (plain wax) isn't what is seen. Both bob.
        if (OutKind == ItemKind.Seal && arCamera != null)
        {
            Vector3 toUser = arCamera.transform.position - piece.position;
            toUser.y = 0f;
            if (toUser.sqrMagnitude > 0.0001f)
                piece.rotation = Quaternion.LookRotation(toUser.normalized, Vector3.up) *
                                 Quaternion.Euler(0f, Mathf.Sin(Time.unscaledTime * 0.9f) * sealSwayDegrees, 0f);
        }
        else
        {
            piece.Rotate(0f, turnDegreesPerSecond * Time.unscaledDeltaTime, 0f, Space.Self);
        }
        var position = piece.localPosition;
        float bob = bobHeight * Mathf.Max(1f, size.y / pieceLength); // a big in-city piece bobs more
        position.y = baseHeight + Mathf.Sin(Time.unscaledTime * 2f) * bob;
        piece.localPosition = position;

        bool aimed = IsAimedAt(piece.position);
        if (aimed)
        {
            // A gentle pulse of light: between a little and fully aimedTint, so the item still looks like itself.
            float pulse = 0.5f - 0.5f * Mathf.Cos(Time.unscaledTime * Mathf.PI * 2f * aimedPulsesPerSecond);
            SetTint(Color.Lerp(Color.white, aimedTint, 0.35f + 0.65f * pulse));
        }
        if (aimed != IsAimed)
        {
            IsAimed = aimed;
            if (!aimed) SetTint(Color.white);
            Changed?.Invoke();
        }
        else if (Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + refreshSeconds;
            Changed?.Invoke(); // the direction hint follows the user
        }
    }

    /// The screen centre's ray passes within aimTolerance of the point, in reach.
    bool IsAimedAt(Vector3 point)
    {
        if (arCamera == null) return false;
        Ray ray = arCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 toPoint = point - ray.origin;
        float along = Vector3.Dot(toPoint, ray.direction);
        if (along <= 0f || along > reach) return false;
        return (toPoint - ray.direction * along).sqrMagnitude <= tolerance * tolerance;
    }

    void SetTint(Color tint)
    {
        if (pieceRenderer == null) return;
        block ??= new MaterialPropertyBlock();
        Color baseColor = itemMaterial != null && itemMaterial.HasProperty(BaseColorId)
            ? itemMaterial.GetColor(BaseColorId)
            : Color.white;
        block.SetColor(BaseColorId, baseColor * tint);
        pieceRenderer.SetPropertyBlock(block);
    }

    // Removing
    void RemovePiece()
    {
        spawnVersion++;
        IsSpawning = false;
        collecting = false;
        IsAimed = false;
        shownPiece = -1;
        if (piece != null) Destroy(piece.gameObject);
        piece = null;
        pieceRenderer = null;
        RemoveAnchor(pieceAnchor, pieceRoot);
        pieceAnchor = null;
        pieceRoot = null;
    }

    void RemoveAnchor(ARAnchor anchor, Transform root)
    {
        if (anchor != null && anchorManager != null && anchorManager.enabled && anchorManager.subsystem != null &&
            anchorManager.TryRemoveAnchor(anchor))
            return;
        if (root != null) Destroy(root.gameObject);
    }
}
