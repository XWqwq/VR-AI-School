using UnityEngine;

public class MouseLook : MonoBehaviour
{
    [Header("视角设置")]
    public float mouseSensitivity = 2.0f;
    public float smoothTime = 0.03f;
    
    [Header("视角限制")]
    public bool clampVerticalRotation = true;
    public float minYAngle = -90f;
    public float maxYAngle = 90f;
    
    // 目标旋转角度（累积输入）
    private Vector2 targetRotation;
    // 当前平滑旋转角度
    private Vector2 currentRotation;
    private Vector2 rotationVelocity;
    
    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        
        // 初始化当前和目标为物体初始角度
        Vector3 startEuler = transform.localEulerAngles;
        currentRotation.x = startEuler.y;
        currentRotation.y = startEuler.x;
        targetRotation = currentRotation;
    }
    
    void Update()
    {
        HandleMouseLook();
    }
    
    void HandleMouseLook()
    {
        // 获取鼠标输入
        Vector2 mouseInput = new Vector2(
            Input.GetAxis("Mouse X"),
            Input.GetAxis("Mouse Y")
        );
        
        // 应用灵敏度并累加到目标旋转
        targetRotation.x += mouseInput.x * mouseSensitivity;
        targetRotation.y += mouseInput.y * mouseSensitivity;
        
        // 限制垂直目标角度（避免无限制累积）
        if (clampVerticalRotation)
        {
            targetRotation.y = Mathf.Clamp(targetRotation.y, minYAngle, maxYAngle);
        }
        
        // 平滑地从当前旋转移向目标旋转
        currentRotation.x = Mathf.SmoothDamp(currentRotation.x, targetRotation.x, ref rotationVelocity.x, smoothTime);
        currentRotation.y = Mathf.SmoothDamp(currentRotation.y, targetRotation.y, ref rotationVelocity.y, smoothTime);
        
        // 应用旋转（注意：垂直方向取负，实现“鼠标向上=视角向上”的直觉）
        transform.localRotation = Quaternion.Euler(-currentRotation.y, currentRotation.x, 0f);
    }
    
    public void ToggleCursorLock()
    {
        if (Cursor.lockState == CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}