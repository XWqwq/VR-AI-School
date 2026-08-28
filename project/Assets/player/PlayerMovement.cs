using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [Header("移动设置")]
    public float walkSpeed = 3.0f;
    public float runSpeed = 6.0f;
    public float smoothTurnTime = 0.1f;
    
    [Header("组件引用")]
    public CharacterController characterController;
    public Transform cameraTransform;
    
    private Vector2 inputDirection;
    private float turnSmoothVelocity;
    private bool isRunning = false;
    
    void Update()
    {
        //键盘输入
        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");
        inputDirection = new Vector2(horizontal, vertical).normalized;
        //判断疾跑状态
        isRunning = Input.GetKey(KeyCode.LeftShift) && inputDirection.magnitude > 0.1f;
        //移动角色
        if (inputDirection.magnitude >= 0.1f)
        {
            float targetAngle = Mathf.Atan2(inputDirection.x, inputDirection.y) * Mathf.Rad2Deg + cameraTransform.eulerAngles.y;
            Vector3 moveDir = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;
            float currentSpeed = isRunning ? runSpeed : walkSpeed;
            characterController.Move(moveDir.normalized * currentSpeed * Time.deltaTime);
        }
        if (Input.GetKey(KeyCode.LeftAlt))
        {
            UnlockCursor();
        }
        else
        {
            LockCursor();
        }
    }
    void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked; // 将光标锁定在屏幕中央
        Cursor.visible = false; // 隐藏光标
    }
    void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None; // 解锁光标
        Cursor.visible = true; // 显示光标
    }
}