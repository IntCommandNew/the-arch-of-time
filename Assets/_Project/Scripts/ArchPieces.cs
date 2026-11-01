using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Arch pieces (test mode for the quest's collecting, until the destinations and the arch model exist).
///
/// Spawn(): a placeholder piece (a stone block) appears at a random spot 1.5 to 4 m around the user,
/// in any direction (also behind), floating ~1 m above the detected floor and anchored in AR so it
/// stays put. It slowly turns and bobs. The centre of the screen is the aim: when it is on a piece
/// within reach, the piece turns gold (IsAimed) and Collect() takes it (it spins away). One piece is
/// out at a time; Spawn() again moves it. The collected count is remembered (PlayerPrefs).
///
/// No UI of its own: AppController shows the count, the hint and the buttons.
/// </summary>
public class ArchPieces : MonoBehaviour
{
    [Header("AR (XR Origin and Main Camera)")]
    [SerializeField] ARRaycastManager raycastManager;
    [SerializeField] ARAnchorManager anchorManager;
    [SerializeField] ARPlaneManager planeManager;
    [SerializeField] Camera arCamera;

    [Header("Piece (placeholder)")]
    [Tooltip("The academy's stone material (m_stone), so the placeholder matches the building.")]
    [SerializeField] Material pieceMaterial;
    [Tooltip("Size of the placeholder block (metres).")]
    [SerializeField] Vector3 pieceSize = new Vector3(0.3f, 0.2f, 0.15f);
    [Tooltip("Multiplies the stone colour while aimed at. Above 1 brightens it: a plain gold (<= 1) only " +
             "darkened the beige stone to brown, which didn't read as a highlight.")]
    [SerializeField] Color aimedTint = new Color(2.2f, 1.7f, 0.7f);
    [SerializeField] float turnDegreesPerSecond = 30f;
    [SerializeField] float bobHeight = 0.05f;

    [Header("Spawning")]
    [SerializeField] int totalPieces = 5;
    [SerializeField] float minDistance = 1.5f;
    [SerializeField] float maxDistance = 4f;
    [Tooltip("Height of the piece above the floor (metres).")]
    [SerializeField] float heightAboveFloor = 1f;
    [Tooltip("Floor height below the camera when no floor has been detected (metres).")]
    [SerializeField] float fallbackFloorBelowCamera = 1.4f;

    [Header("Collecting")]
    [Tooltip("Farthest a piece can be collected from (metres).")]
    [SerializeField] float collectDistance = 6f;
    [Tooltip("How far (metres) the aim may pass from the piece's centre and still count.")]
    [SerializeField] float aimTolerance = 0.25f;
    [SerializeField] float collectSeconds = 0.4f;
    [Tooltip("Seconds between Changed events while a piece is out (keeps the direction hint current).")]
    [SerializeField] float refreshSeconds = 0.3f;

    /// <summary>Count, piece, aim or direction changed.</summary>
    public event Action Changed;

    public int Collected { get; private set; }
    public int Total => totalPieces;
    public bool AllCollected => Collected >= totalPieces;
    public bool HasPiece => piece != null && !collecting;
    public bool IsAimed { get; private set; }
    public bool IsSpawning { get; private set; }

    const string CollectedKey = "ArchPieces.Collected";
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();

    bool active;
    int spawnVersion;           // bumped by RemovePiece(), so a spawn still in progress is thrown away
    ARAnchor pieceAnchor;
    Transform pieceRoot;        // the anchor (in the Editor, a stand-in)
    Transform piece;
    Renderer pieceRenderer;
    MaterialPropertyBlock block;
    bool collecting;
    float collectStart;
    float nextRefresh;

    void Awake()
    {
        Collected = Mathf.Clamp(PlayerPrefs.GetInt(CollectedKey, 0), 0, totalPieces);
    }

    /// <summary>Entering the pieces mode.</summary>
    public void Begin()
    {
        active = true;
        Changed?.Invoke();
    }

    /// <summary>Leaving the pieces mode: the piece that is out goes away.</summary>
    public void End()
    {
        active = false;
        RemovePiece();
    }

    public void ResetProgress()
    {
        Collected = 0;
        PlayerPrefs.SetInt(CollectedKey, 0);
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    // Spawning
    /// <summary>Puts a piece at a random spot around the user (replacing the one that is out).</summary>
    public async void Spawn()
    {
        if (!active || IsSpawning || AllCollected || arCamera == null) return;
        RemovePiece();
        IsSpawning = true;
        int version = spawnVersion;
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
                    Debug.LogWarning($"[ArchPieces] The piece's anchor could not be created ({result.status}).");
                }
            }
#if UNITY_EDITOR
            else
            {
                root = new GameObject("Piece anchor (Editor stand-in)").transform;
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
            Debug.Log($"[ArchPieces] Piece spawned {Vector3.Distance(cameraPosition, spot):0.0} m away.");
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

    Transform CreatePiece(Transform root)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Arch piece (placeholder)";
        Destroy(go.GetComponent<Collider>()); // aiming is a ray test, no physics
        go.transform.SetParent(root, false);
        go.transform.localPosition = new Vector3(0f, heightAboveFloor, 0f);
        go.transform.localScale = pieceSize;
        pieceRenderer = go.GetComponent<MeshRenderer>();
        if (pieceMaterial != null) pieceRenderer.sharedMaterial = pieceMaterial;
        block ??= new MaterialPropertyBlock();
        SetTint(Color.white);
        return go.transform;
    }

    // Aiming and collecting
    /// <summary>Collects the piece being aimed at (it spins away, the count goes up).</summary>
    public void Collect()
    {
        if (!HasPiece || !IsAimed) return;
        collecting = true;
        collectStart = Time.unscaledTime;
        IsAimed = false;
        Changed?.Invoke();
    }

    /// <summary>Where the piece is from the user's view: angle (positive = right), distance, on screen.</summary>
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
            piece.localScale = pieceSize * (1f - t);
            piece.Rotate(0f, 720f * Time.unscaledDeltaTime, 0f, Space.Self);
            piece.localPosition += Vector3.up * (0.6f * Time.unscaledDeltaTime);
            if (t >= 1f)
            {
                RemovePiece();
                Collected = Mathf.Min(Collected + 1, totalPieces);
                PlayerPrefs.SetInt(CollectedKey, Collected);
                PlayerPrefs.Save();
                Debug.Log($"[ArchPieces] Piece collected: {Collected} / {totalPieces}.");
                Changed?.Invoke();
            }
            return;
        }

        // Idle animation: slow turn and bob.
        piece.Rotate(0f, turnDegreesPerSecond * Time.unscaledDeltaTime, 0f, Space.Self);
        var position = piece.localPosition;
        position.y = heightAboveFloor + Mathf.Sin(Time.unscaledTime * 2f) * bobHeight;
        piece.localPosition = position;

        bool aimed = IsAimedAt(piece.position);
        if (aimed != IsAimed)
        {
            IsAimed = aimed;
            SetTint(aimed ? aimedTint : Color.white);
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
        if (along <= 0f || along > collectDistance) return false;
        return (toPoint - ray.direction * along).sqrMagnitude <= aimTolerance * aimTolerance;
    }

    void SetTint(Color tint)
    {
        if (pieceRenderer == null) return;
        block ??= new MaterialPropertyBlock();
        Color baseColor = pieceMaterial != null && pieceMaterial.HasProperty(BaseColorId)
            ? pieceMaterial.GetColor(BaseColorId)
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
