using UnityEngine;

public class BeamController : MonoBehaviour
{
    [Header("光柱设置")]
    public float beamHeight = 10f;
    public Color beamColor = Color.cyan;
    public float pulseSpeed = 2f;
    public float minWidth = 0.3f;
    public float maxWidth = 1.2f;
    
    private LineRenderer lineRenderer;
    private Material beamMaterial;
    
    void Start()
    {
        lineRenderer = GetComponent<LineRenderer>();
        if (lineRenderer == null)
        {
            Debug.LogError("BeamController: 找不到LineRenderer组件！");
            return;
        }
        
        // 获取或创建材质实例
        beamMaterial = lineRenderer.material;
        if (beamMaterial == null)
        {
            beamMaterial = new Material(Shader.Find("Particles/Standard Unlit"));
            lineRenderer.material = beamMaterial;
        }
        
        UpdateBeamGeometry();
    }
    
    void Update()
    {
        // 脉动效果
        if (lineRenderer != null)
        {
            float pulse = (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f;
            float currentWidth = Mathf.Lerp(minWidth, maxWidth, pulse);
            
            lineRenderer.startWidth = currentWidth * 0.3f;
            lineRenderer.endWidth = currentWidth;
            
            // 颜色脉动
            Color pulsedColor = beamColor;
            pulsedColor.a = 0.8f + 0.2f * pulse;
            
            if (beamMaterial != null)
            {
                beamMaterial.color = pulsedColor;
            }
        }
    }
    
    void UpdateBeamGeometry()
    {
        if (lineRenderer != null)
        {
            lineRenderer.positionCount = 2;
            lineRenderer.SetPosition(0, Vector3.zero);
            lineRenderer.SetPosition(1, new Vector3(0, beamHeight, 0));
        }
    }
    
    // 公开方法，供BuildingInfo调用
    public void SetBeamColor(Color newColor)
    {
        beamColor = newColor;
        if (beamMaterial != null)
        {
            beamMaterial.color = newColor;
        }
    }
    
    public void SetBeamHeight(float newHeight)
    {
        beamHeight = newHeight;
        UpdateBeamGeometry();
    }
}