using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Starts the final classroom lesson only after the guided route reaches the
/// classroom teaching area. The teacher delivers the existing lecture audio while PPT
/// slides are displayed on the blackboard and adjacent black frame.
/// </summary>
public sealed class CampusClassroomLessonService : MonoBehaviour
{
    public static CampusClassroomLessonService Instance { get; private set; }
    private const string LessonAudioResource = "ClassroomLesson/audio";

    private AudioSource teacherAudio;
    private Transform teacher;
    private GameObject blackboardScreen;
    private Texture2D[] slides = Array.Empty<Texture2D>();
    private bool routeArrived;
    private bool lessonStarted;
    private int slideIndex;
    private InputAction startLessonAction;
    private InputAction stopLessonAction;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
        if (CampusNavigationService.Instance != null)
            CampusNavigationService.Instance.NavigationCompleted += OnNavigationCompleted;
        startLessonAction = new InputAction("Start Classroom Lesson", InputActionType.Button,
            "<XRController>{LeftHand}/primaryButton"); // X
        stopLessonAction = new InputAction("Stop Classroom Lesson", InputActionType.Button,
            "<XRController>{LeftHand}/secondaryButton"); // Y
        startLessonAction.Enable();
        stopLessonAction.Enable();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (CampusNavigationService.Instance != null)
            CampusNavigationService.Instance.NavigationCompleted -= OnNavigationCompleted;
        startLessonAction?.Dispose();
        stopLessonAction?.Dispose();
        ClearScreens();
        if (Instance == this) Instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (teacherAudio != null) teacherAudio.Stop();
        lessonStarted = false;
        routeArrived = false;
        ClearScreens();
        if (scene.buildIndex == 1)
            StartCoroutine(PrepareClassroom());
    }

    private IEnumerator PrepareClassroom()
    {
        yield return null;
        // Ask the persistent presenter to create/show the teacher here as well, so a
        // classroom entry never depends on scene callback ordering.
        teacher = CampusHolographicAvatarPresenter.Instance?.EnsureClassroomTeacher();
        if (teacher == null)
            teacher = GameObject.Find("智慧教室老师")?.transform;
        if (teacher == null)
        {
            Debug.LogWarning("课堂讲解未启动：未找到老师模型。");
            yield break;
        }

        teacherAudio = teacher.GetComponentInChildren<AudioSource>(true);
        if (teacherAudio == null)
            teacherAudio = teacher.gameObject.AddComponent<AudioSource>();
        var lessonClip = Resources.Load<AudioClip>(LessonAudioResource);
        if (lessonClip == null)
        {
            Debug.LogWarning("课堂讲解未启动：未找到 Resources/ClassroomLesson/audio.mp3。");
            yield break;
        }
        // Always replace the prefab's demonstration clip with the course audio
        // supplied for this PPT, so slides and narration use one lesson source.
        teacherAudio.Stop();
        teacherAudio.clip = lessonClip;
        teacherAudio.playOnAwake = false;
        teacherAudio.spatialBlend = 1f;
        teacherAudio.minDistance = 1.5f;
        teacherAudio.maxDistance = 18f;

        slides = Resources.LoadAll<Texture2D>("ClassroomPpt")
            .OrderBy(texture => SlideNumber(texture.name))
            .ToArray();
        if (slides.Length == 0)
        {
            Debug.LogWarning("课堂讲解未启动：未找到 ClassroomPpt 课件图片。");
            yield break;
        }

        CreateScreens();
        // Display the first slide immediately. The player explicitly starts
        // the lesson with the left-controller X button.
        slideIndex = 0;
        ApplySlides();
        blackboardScreen?.SetActive(true);
        routeArrived = true;
    }

    private void Update()
    {
        if (SceneManager.GetActiveScene().buildIndex != 1)
            return;
        if (stopLessonAction != null && stopLessonAction.WasPressedThisFrame())
            StopLesson();
        else if (startLessonAction != null && startLessonAction.WasPressedThisFrame())
            StartLesson();

        if (lessonStarted)
            SynchronizeSlidesToAudio();
    }

    private void OnNavigationCompleted(string targetId)
    {
        if (SceneManager.GetActiveScene().buildIndex != 1)
            return;
        routeArrived = true;
    }

    private void StartLesson()
    {
        if (lessonStarted || !routeArrived || teacher == null || slides.Length == 0)
            return;
        lessonStarted = true;
        slideIndex = 0;
        ApplySlides();
        blackboardScreen?.SetActive(true);
        if (teacherAudio != null && teacherAudio.clip != null)
        {
            teacherAudio.time = 0f;
            teacherAudio.Play();
        }
        CampusTaskService.Instance?.Report(CampusTaskTrigger.LessonStarted);
        CampusFeedbackService.Instance?.TaskComplete();
    }

    private void StopLesson()
    {
        if (!lessonStarted) return;
        if (teacherAudio != null) teacherAudio.Stop();
        lessonStarted = false;
        slideIndex = 0;
        ApplySlides();
    }

    private void SynchronizeSlidesToAudio()
    {
        if (teacherAudio == null || teacherAudio.clip == null || slides.Length == 0)
            return;

        // Each slide owns an equal portion of the teacher's prepared audio.
        // This keeps the displayed material tied to the spoken progress rather
        // than drifting on an unrelated fixed timer.
        var progress = Mathf.Clamp01(teacherAudio.time / teacherAudio.clip.length);
        var synchronizedIndex = Mathf.Min(slides.Length - 1,
            Mathf.FloorToInt(progress * slides.Length));
        if (synchronizedIndex != slideIndex)
        {
            slideIndex = synchronizedIndex;
            ApplySlides();
        }

        if (!teacherAudio.isPlaying && teacherAudio.time >= teacherAudio.clip.length - 0.05f)
            lessonStarted = false;
    }

    private void CreateScreens()
    {
        var presentationCube = GameObject.Find("Cube");
        var hasPresentationCube = presentationCube != null &&
                                  presentationCube.scene == SceneManager.GetActiveScene();
        var primaryPose = hasPresentationCube
            ? GetCubeScreenPose(presentationCube.transform)
            : FindScreenPose(Vector3.zero, new Vector3(3.6f, 2.03f, 1f));
        var primarySize = hasPresentationCube
            ? GetCubeScreenSize(presentationCube.GetComponent<Renderer>())
            : new Vector3(3.6f, 2.03f, 1f);
        blackboardScreen = CreateScreen("课堂 PPT · 黑板中央", primaryPose.position, primaryPose.rotation,
            primarySize);
    }

    private static (Vector3 position, Quaternion rotation) GetCubeScreenPose(Transform cube)
    {
        var renderer = cube.GetComponent<Renderer>();
        var bounds = renderer.bounds;
        // The teacher and players are on Cube's +X side. Offset the quad by a
        // few millimetres so it draws on the face rather than inside the cube.
        var faceNormal = cube.right.normalized;
        var faceOffset = Mathf.Abs(Vector3.Dot(bounds.extents, faceNormal)) + 0.006f;
        // The audience looks toward -X, so the quad front must face back toward
        // them; otherwise the slide would only be visible through the un-culled
        // back face and appear mirrored.
        var facing = Quaternion.LookRotation(faceNormal, cube.up) * Quaternion.Euler(0f, 180f, 0f);
        return (bounds.center + faceNormal * faceOffset, facing);
    }

    private static Vector3 GetCubeScreenSize(Renderer cubeRenderer)
    {
        var bounds = cubeRenderer.bounds;
        // Cube is thin on X, so its Z/Y dimensions define the PPT surface.
        // Fit the 16:9 slide inside that face without stretching it.
        var maxWidth = bounds.size.z * 0.96f;
        var maxHeight = bounds.size.y * 0.96f;
        var height = Mathf.Min(maxHeight, maxWidth / (16f / 9f));
        return new Vector3(height * (16f / 9f), height, 1f);
    }

    private (Vector3 position, Quaternion rotation) FindScreenPose(Vector3 lateralOffset, Vector3 size)
    {
        var origin = teacher.position + Vector3.up * 1.55f + lateralOffset;
        var direction = -teacher.forward;
        var hits = Physics.RaycastAll(origin, direction, 8f, ~0, QueryTriggerInteraction.Ignore)
            .OrderBy(hit => hit.distance);
        foreach (var hit in hits)
        {
            // Do not project the image onto the teacher's own mesh/collider.
            if (hit.transform == teacher || hit.transform.IsChildOf(teacher))
                continue;
            return (hit.point + hit.normal * 0.025f, Quaternion.LookRotation(hit.normal, Vector3.up));
        }

        // The imported classroom mesh has no dependable blackboard collider on
        // every platform. This puts the fallback at the board's visual center,
        // behind the teacher instead of inside the podium or wall.
        var fallback = origin + direction * 1.25f;
        return (fallback, Quaternion.LookRotation(-direction, Vector3.up));
    }

    private static GameObject CreateScreen(string name, Vector3 position, Quaternion rotation, Vector3 size)
    {
        var screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
        screen.name = name;
        screen.transform.SetPositionAndRotation(position, rotation);
        screen.transform.localScale = size;
        Destroy(screen.GetComponent<Collider>());
        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
        var renderer = screen.GetComponent<Renderer>();
        var material = new Material(shader);
        // Imported classroom boards can be viewed from either side depending
        // on the model's winding. Disable culling so the PPT never vanishes
        // solely because the generated quad faces the opposite direction.
        if (material.HasProperty("_Cull")) material.SetInt("_Cull", 0);
        material.color = Color.white;
        renderer.material = material;
        return screen;
    }

    private void ApplySlides()
    {
        ApplyTexture(blackboardScreen, slides[slideIndex]);
    }

    private static void ApplyTexture(GameObject screen, Texture2D texture)
    {
        if (screen == null || texture == null) return;
        var material = screen.GetComponent<Renderer>().material;
        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
        if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
    }

    private void ClearScreens()
    {
        if (blackboardScreen != null) Destroy(blackboardScreen);
        blackboardScreen = null;
    }

    private static int SlideNumber(string name)
    {
        var separator = name.LastIndexOf('-');
        return separator >= 0 && int.TryParse(name.Substring(separator + 1), out var number)
            ? number : int.MaxValue;
    }
}
