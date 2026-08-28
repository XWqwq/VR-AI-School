using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class CampusHolographicAvatarPresenter : MonoBehaviour
{
    private const string TeacherObjectName = "智慧教室老师";
    // Use the authored teacher position from the original classroom scene.
    // The teaching-area hotspot is only a navigation marker, not the podium.
    private static readonly Vector3 ClassroomTeacherPosition = new Vector3(-4.693f, 0.22f, 18.147f);
    private static readonly Quaternion ClassroomTeacherRotation = Quaternion.Euler(0f, 90f, 0f);

    public static CampusHolographicAvatarPresenter Instance { get; private set; }
    private GameObject avatarRoot;
    private Animator boundAnimator;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (CampusAICompanionService.Instance != null)
            CampusAICompanionService.Instance.VisibilityChanged += OnVisibilityChanged;
        LoadFormalAvatar();
        if (avatarRoot != null) avatarRoot.SetActive(SceneManager.GetActiveScene().buildIndex == 1);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void LoadFormalAvatar()
    {
        var avatarPrefab = Resources.Load<GameObject>("Teachers/Teacher");
        if (avatarPrefab == null)
        {
            Debug.LogError("老师预制体加载失败：Resources/Teachers/Teacher.prefab 不存在。");
            return;
        }
        var avatar = Instantiate(avatarPrefab);
        avatar.transform.localPosition = Vector3.zero;
        avatar.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        avatar.transform.localScale = Vector3.one * 0.85f;
        foreach (var audio in avatar.GetComponentsInChildren<AudioSource>(true))
        {
            audio.playOnAwake = false;
            audio.Stop();
        }
        BindFormalAvatar(avatar);
    }

    private void OnDestroy()
    {
        if (CampusAICompanionService.Instance != null)
            CampusAICompanionService.Instance.VisibilityChanged -= OnVisibilityChanged;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this) Instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (avatarRoot != null && scene.buildIndex == 1)
        {
            avatarRoot.SetActive(true);
            if (boundAnimator != null) boundAnimator.SetTrigger("Greet");
        }
        else if (avatarRoot != null) avatarRoot.SetActive(false);
    }

    public void BindFormalAvatar(GameObject model)
    {
        if (model == null) return;
        if (avatarRoot != null) Destroy(avatarRoot);
        avatarRoot = model;
        avatarRoot.name = TeacherObjectName;
        DontDestroyOnLoad(avatarRoot);
        boundAnimator = avatarRoot.GetComponentInChildren<Animator>();
        avatarRoot.SetActive(true);
    }

    /// <summary>Ensures the formal teacher model is present and anchored on the classroom podium.</summary>
    public Transform EnsureClassroomTeacher()
    {
        if (avatarRoot == null)
            LoadFormalAvatar();
        if (avatarRoot == null)
            return null;

        avatarRoot.SetActive(true);
        avatarRoot.transform.SetPositionAndRotation(ClassroomTeacherPosition, ClassroomTeacherRotation);
        return avatarRoot.transform;
    }

    private void Update()
    {
        if (avatarRoot == null || !avatarRoot.activeSelf) return;

        if (TryAnchorAtClassroomPodium())
            return;
        
        var companion = CampusAICompanionService.Instance;
        if (companion != null && companion.BubbleTransform != null)
        {
            var bubble = companion.BubbleTransform;
            avatarRoot.transform.position = bubble.TransformPoint(new Vector3(-0.53f, -0.08f, 0.04f));
            avatarRoot.transform.rotation = bubble.rotation;
        }
        else
        {
            var head = Camera.main?.transform;
            if (head != null)
            {
                var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
                if (forward.sqrMagnitude < 0.1f) forward = Vector3.forward;
                avatarRoot.transform.position = head.position + forward * 1.4f + Vector3.up * 0.2f;
                avatarRoot.transform.rotation = Quaternion.LookRotation(
                    avatarRoot.transform.position - head.position, Vector3.up);
            }
        }
        
        var breathe = 1f + Mathf.Sin(Time.unscaledTime * 2.2f) * 0.018f;
        if (boundAnimator == null) avatarRoot.transform.localScale = Vector3.one * breathe;
    }

    private bool TryAnchorAtClassroomPodium()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.buildIndex != 1) return false;

        avatarRoot.transform.SetPositionAndRotation(ClassroomTeacherPosition, ClassroomTeacherRotation);
        return true;
    }

    private static Transform FindNamedTransform(Scene scene, string name)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var result = FindNamedChild(root.transform, name);
            if (result != null) return result;
        }
        return null;
    }

    private static Transform FindNamedChild(Transform parent, string name)
    {
        if (parent.name == name) return parent;
        for (var i = 0; i < parent.childCount; i++)
        {
            var result = FindNamedChild(parent.GetChild(i), name);
            if (result != null) return result;
        }
        return null;
    }

    private void OnVisibilityChanged(bool visible)
    {
        if (visible && boundAnimator != null)
            boundAnimator.SetTrigger("Greet");
    }

}
