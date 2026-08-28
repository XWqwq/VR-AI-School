using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum CampusTaskTrigger
{
    AttachLetter, OpenLetter, OpenMenuPage, VisitScene,
    FocusLocation, AskQuestion, CollectStamp, StartNavigation, Move, LessonStarted
}

[Serializable]
public sealed class CampusTaskDefinition
{
    public string id;
    public string title;
    public CampusTaskTrigger trigger;
    public string target;
}

public sealed class CampusTaskService : MonoBehaviour
{
    public static CampusTaskService Instance { get; private set; }
    public event Action<CampusTaskDefinition> TaskCompleted;
    public IReadOnlyList<CampusTaskDefinition> Tasks => tasks;

    private readonly List<CampusTaskDefinition> tasks = new List<CampusTaskDefinition>
    {
        new CampusTaskDefinition { id = "tutorial_listen_class", title = "按左手 X 开始老师讲课", trigger = CampusTaskTrigger.LessonStarted },
        new CampusTaskDefinition { id = "tutorial_attach_letter", title = "吸附录取通知书", trigger = CampusTaskTrigger.AttachLetter },
        new CampusTaskDefinition { id = "tutorial_open_letter", title = "打开录取通知书", trigger = CampusTaskTrigger.OpenLetter },
        new CampusTaskDefinition { id = "tutorial_move", title = "在VR中移动", trigger = CampusTaskTrigger.Move },
        new CampusTaskDefinition { id = "tutorial_open_map", title = "查看校园地图", trigger = CampusTaskTrigger.OpenMenuPage, target = "map" },
        new CampusTaskDefinition { id = "tutorial_start_navigation", title = "启动一次地图导航", trigger = CampusTaskTrigger.StartNavigation },
        new CampusTaskDefinition { id = "tutorial_scan", title = "使用 AI 空间扫描", trigger = CampusTaskTrigger.OpenMenuPage, target = "scan" },
        new CampusTaskDefinition { id = "tutorial_ask_ai", title = "完成第一次 AI 提问", trigger = CampusTaskTrigger.AskQuestion },
        new CampusTaskDefinition { id = "tutorial_open_tasks", title = "查看任务中心", trigger = CampusTaskTrigger.OpenMenuPage, target = "tasks" },
        new CampusTaskDefinition { id = "tutorial_open_collection", title = "查看探索图鉴", trigger = CampusTaskTrigger.OpenMenuPage, target = "collection" },
        new CampusTaskDefinition { id = "tutorial_open_report", title = "查看我的第一日", trigger = CampusTaskTrigger.OpenMenuPage, target = "report" },
        new CampusTaskDefinition { id = "tutorial_open_exhibit", title = "查看 AI 生成式展项", trigger = CampusTaskTrigger.OpenMenuPage, target = "exhibit" },
        new CampusTaskDefinition { id = "tutorial_open_memory", title = "查看 AI 记忆与隐私", trigger = CampusTaskTrigger.OpenMenuPage, target = "memory" },
        new CampusTaskDefinition { id = "tutorial_visit_classroom", title = "通过通知书进入智慧教室", trigger = CampusTaskTrigger.VisitScene, target = "1" },
        new CampusTaskDefinition { id = "tutorial_visit_exhibition", title = "通过通知书进入 AI 展厅", trigger = CampusTaskTrigger.VisitScene, target = "2" },
        new CampusTaskDefinition { id = "open_collection", title = "查看探索图鉴", trigger = CampusTaskTrigger.OpenMenuPage, target = "collection" },
        new CampusTaskDefinition { id = "open_first_day", title = "查看我的第一日纪念页", trigger = CampusTaskTrigger.OpenMenuPage, target = "report" },
        new CampusTaskDefinition { id = "visit_classroom", title = "参观教室", trigger = CampusTaskTrigger.VisitScene, target = "1" },
        new CampusTaskDefinition { id = "visit_exhibition", title = "参观展厅", trigger = CampusTaskTrigger.VisitScene, target = "2" },
        new CampusTaskDefinition { id = "discover_location", title = "发现一个校园地点", trigger = CampusTaskTrigger.FocusLocation },
        new CampusTaskDefinition { id = "ask_ai_question", title = "向 AI 向导提问", trigger = CampusTaskTrigger.AskQuestion },
        new CampusTaskDefinition { id = "collect_first_stamp", title = "获得第一枚探索印章", trigger = CampusTaskTrigger.CollectStamp }
    };

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        if (Instance != this) return;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Report(CampusTaskTrigger.VisitScene, scene.buildIndex.ToString());
    }

    public void Report(CampusTaskTrigger trigger, string target = "")
    {
        var progress = CampusProgressService.Instance;
        if (progress == null) return;
        foreach (var task in tasks)
        {
            if (task.trigger != trigger || progress.IsTaskCompleted(task.id)) continue;
            if (!string.IsNullOrEmpty(task.target) && task.target != target) continue;
            progress.CompleteTask(task.id);
            TaskCompleted?.Invoke(task);
        }
    }
}
