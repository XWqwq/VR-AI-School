using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class CampusTestHotspotBootstrap : MonoBehaviour
{
    public static CampusTestHotspotBootstrap Instance { get; private set; }
    private bool corridorBoardHandled;

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
        PanelGrabService.PanelPickedUp += OnPanelPickedUp;
        if (CampusNavigationService.Instance != null)
            CampusNavigationService.Instance.NavigationCompleted += OnNavigationCompleted;
        RegisterCrossSceneDestinations();
        CreateForScene(SceneManager.GetActiveScene());
    }

    private void Start()
    {
        // Retry once on the first frame: services are created in a fixed order
        // at startup, but a scene-placed instance may have loaded before the
        // location registry existed.
        RegisterCrossSceneDestinations();
    }

    private static void RegisterCrossSceneDestinations()
    {
        if (CampusLocationRegistry.Instance == null)
            return;
        CampusLocationRegistry.Instance.Register(new CampusLocationDefinition
        {
            id = "main_dongwu_gate", displayName = "东吴门",
            description = "东吴门是苏州大学未来校区的代表性校园景观，也是本次虚拟新生导览的起点。完成注视确认后可获得主线印章。",
            sceneIndex = 0, navigationTarget = new Vector3(0f, 0f, 4f),
            category = "campus_landmark", grantsStamp = true
        });
        CampusLocationRegistry.Instance.Register(new CampusLocationDefinition
        {
            id = "main_innovation_corridor", displayName = "科创文化连廊",
            description = "文化连廊位于未来科创中心一楼，集中展示学生科创成果、科研项目和校园资讯。",
            sceneIndex = 0, navigationTarget = new Vector3(-20.670f, 2.43f, -148.57f),
            category = "innovation_exhibition", grantsStamp = true
        });
        CampusLocationRegistry.Instance.Register(new CampusLocationDefinition
        {
            id = "main_corridor_exit", displayName = "连廊终点展板",
            description = "完成展板参观后，从这里前往 AI 展厅。",
            sceneIndex = 0, navigationTarget = new Vector3(-20.670f, 2.43f, -148.57f),
            category = "navigation_anchor", grantsStamp = false
        });
        CampusLocationRegistry.Instance.Register(new CampusLocationDefinition
        {
            id = "classroom_teaching_area", displayName = "智慧教学区",
            description = "智慧教学区用于展示沉浸式课堂、虚拟实验和教学互动。", sceneIndex = 1,
            navigationTarget = new Vector3(1.8f, 0f, 16.9f), category = "test_hotspot", grantsStamp = true
        });
        CampusLocationRegistry.Instance.Register(new CampusLocationDefinition
        {
            id = "classroom_ai_terminal", displayName = "AI 学习终端",
            description = "AI 学习终端用于演示课程问答、学习建议和课堂知识检索。", sceneIndex = 1,
            navigationTarget = new Vector3(3.0f, 0f, 14.8f), category = "test_hotspot", grantsStamp = true
        });
        CampusLocationRegistry.Instance.Register(new CampusLocationDefinition
        {
            id = "exhibition_entrance", displayName = "展厅入口",
            description = "科技展厅入口连接校园发展、智能应用和创新成果展项。", sceneIndex = 2,
            navigationTarget = new Vector3(5.8f, 0f, -3.7f), category = "test_hotspot", grantsStamp = true
        });
        CampusLocationRegistry.Instance.Register(new CampusLocationDefinition
        {
            id = "exhibition_ai_history", displayName = "AI 发展展项",
            description = "AI 发展展项介绍人工智能的发展历程与校园智能应用。", sceneIndex = 2,
            navigationTarget = new Vector3(7.1f, 0f, -2.2f), category = "test_hotspot", grantsStamp = true
        });
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        PanelGrabService.PanelPickedUp -= OnPanelPickedUp;
        if (CampusNavigationService.Instance != null)
            CampusNavigationService.Instance.NavigationCompleted -= OnNavigationCompleted;
        if (Instance == this) Instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        CreateForScene(scene);
    }

    private static void CreateForScene(Scene scene)
    {
        if (scene.buildIndex == 0)
        {
            ConfigureInnovationCorridor(scene);
            CreateHotspot(scene, "main_dongwu_gate", "东吴门",
                "东吴门是苏州大学未来校区的代表性校园景观，也是本次虚拟新生导览的起点。请将视线稳定停留在标记上，等待进度环完成。",
                new Vector3(0f, 1.35f, 4f));
        }
        else if (scene.buildIndex == 1)
        {
            CreateHotspot(scene, "classroom_teaching_area", "智慧教学区",
                "这里用于展示沉浸式课堂、虚拟实验和教学互动。",
                new Vector3(1.8f, 1.35f, 16.9f));
            CreateHotspot(scene, "classroom_ai_terminal", "AI 学习终端",
                "该终端用于演示课程问答、学习建议和课堂知识检索。",
                new Vector3(3.0f, 1.35f, 14.8f));
        }
        else if (scene.buildIndex == 2)
        {
            CreateHotspot(scene, "exhibition_entrance", "展厅入口",
                "这里是虚拟校园主题展厅入口，可查看校园发展与创新成果。",
                new Vector3(5.8f, 1.35f, -3.7f));
            CreateHotspot(scene, "exhibition_ai_history", "AI 发展展项",
                "该展项用于介绍人工智能发展历程和校园智能应用。",
                new Vector3(7.1f, 1.35f, -2.2f));
        }
    }

    private static void ConfigureInnovationCorridor(Scene scene)
    {
        var corridor = GameObject.Find("连廊内");
        if (corridor == null || corridor.scene != scene)
            return;

        var info = corridor.GetComponent<BuildingInfo>();
        if (info != null)
            Destroy(info);

        var endBoard = GameObject.Find("牌11");
        if (endBoard != null && endBoard.scene == scene)
        {
            CampusLocationRegistry.Instance?.Register(new CampusLocationDefinition
            {
                id = "main_corridor_exit", displayName = "连廊终点展板",
                description = "完成展板参观后，从这里前往 AI 展厅。",
                sceneIndex = 0, navigationTarget = endBoard.transform.position,
                category = "navigation_anchor", grantsStamp = false
            });
        }
    }

    private void OnPanelPickedUp(Transform panel)
    {
        if (corridorBoardHandled || panel == null || panel.gameObject.scene.buildIndex != 0 ||
            !panel.name.StartsWith("牌", StringComparison.Ordinal))
            return;
        corridorBoardHandled = true;
        CampusProgressService.Instance?.CompleteLocation("main_innovation_corridor");
        CampusNavigationService.Instance?.StartNavigation("main_corridor_exit");
    }

    private void OnNavigationCompleted(string locationId)
    {
        if (locationId == "main_corridor_exit")
            CampusNavigationService.Instance?.StartNavigation("exhibition_entrance");
    }

    private static void CreateHotspot(
        Scene scene, string id, string displayName, string description, Vector3 position)
    {
        if (GameObject.Find("Test Hotspot - " + id) != null)
            return;
        var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = "Test Hotspot - " + id;
        SceneManager.MoveGameObjectToScene(marker, scene);
        marker.transform.position = position;
        marker.transform.localScale = Vector3.one * 0.22f;
        var renderer = marker.GetComponent<Renderer>();
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var material = new Material(shader);
        var color = new Color(0.08f, 0.75f, 1f, 1f);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", color * 2f);
        renderer.sharedMaterial = material;

        var info = marker.AddComponent<BuildingInfo>();
        info.locationId = id;
        info.buildingName = displayName;
        info.buildingDescription = description;
        info.category = "test_hotspot";
        info.grantsStamp = true;
        info.beamHeight = 2f;
        info.closeDistance = 20f;
    }
}
