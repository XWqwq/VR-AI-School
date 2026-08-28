using UnityEngine;

public sealed class CampusGenerativeExhibitService : MonoBehaviour
{
    public static CampusGenerativeExhibitService Instance { get; private set; }
    public string CurrentTheme { get; private set; } = "尚未生成";
    public string CurrentContent { get; private set; } = "选择一个主题，AI 展厅会立即组合一段可体验的展项说明。";

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void Generate(string theme)
    {
        CurrentTheme = theme;
        switch (theme)
        {
            case "AI 发展之路":
                CurrentContent = "展项路线：感知 → 学习 → 生成 → 智能体。\n互动任务：依次观察四个阶段，并向 AI 询问其中一个阶段如何影响校园。";
                break;
            case "智慧校园一天":
                CurrentContent = "展项路线：入校 → 学习 → 办事 → 生活。\n互动任务：选择最希望 AI 改善的环节，AI 会结合校园知识给出方案。";
                break;
            default:
                CurrentContent = "展项路线：未来课堂 → 虚拟实验 → 个性化学习。\n互动任务：选择感兴趣的课程方向，让 AI 设计一次未来课堂体验。";
                break;
        }
        CampusFeedbackService.Instance?.TaskComplete();
    }

    public string BuildAIQuestion()
    {
        return "请扩展虚拟展项“" + CurrentTheme + "”。当前设计：" + CurrentContent +
               "\n请面向新生给出一段不超过150字的讲解，并提出一个可以在VR中完成的小任务。";
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }
}
