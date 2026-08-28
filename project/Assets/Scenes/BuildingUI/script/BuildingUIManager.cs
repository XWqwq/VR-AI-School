using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BuildingUIManager : MonoBehaviour
{
    public static BuildingUIManager Instance;
    
    [Header("UI组件")]
    public GameObject infoPanel;
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI descriptionText;
    public Image gazeProgressBar;
    
    [Header("动画设置")]
    public float fadeInTime = 0.3f;
    public float fadeOutTime = 0.2f;
    
    private CanvasGroup canvasGroup;
    private BuildingInfo currentBuilding;
    private bool isShowing = false;
    private Coroutine fadeRoutine;
    
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
        
        canvasGroup = infoPanel.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = infoPanel.AddComponent<CanvasGroup>();
        }
        
        HideInfo(); // 初始隐藏
    }
    
    public void ShowBuildingInfo(BuildingInfo building, bool showDetailedInfo)
    {
        currentBuilding = building;
        
        // 更新UI内容
        titleText.text = building.buildingName;
        
        if (showDetailedInfo)
        {
            //descriptionText.text = building.buildingDescription;
            titleText.gameObject.SetActive(true);
        }
        else
        {
            titleText.gameObject.SetActive(false);
        }
        
        // 显示面板
        if (!isShowing)
        {
            isShowing = true;
            infoPanel.SetActive(true);
            if (fadeRoutine != null) StopCoroutine(fadeRoutine);
            fadeRoutine = StartCoroutine(FadePanel(0f, 1f, fadeInTime));
        }
    }
    
    public void HideInfo()
    {
        if (isShowing)
        {
            isShowing = false;
            if (fadeRoutine != null) StopCoroutine(fadeRoutine);
            fadeRoutine = StartCoroutine(FadePanel(1f, 0f, fadeOutTime));
        }
    }
    
    public void UpdateGazeProgress(float progress)
    {
        if (gazeProgressBar != null)
        {
            gazeProgressBar.fillAmount = progress;
        }
    }
    
    private System.Collections.IEnumerator FadePanel(float from, float to, float time)
    {
        float elapsed = 0f;
        
        while (elapsed < time)
        {
            elapsed += Time.deltaTime;
            canvasGroup.alpha = Mathf.Lerp(from, to, elapsed / time);
            yield return null;
        }
        
        canvasGroup.alpha = to;
        
        // 如果完全淡出，隐藏对象
        if (to == 0f)
        {
            infoPanel.SetActive(false);
        }
    }
}