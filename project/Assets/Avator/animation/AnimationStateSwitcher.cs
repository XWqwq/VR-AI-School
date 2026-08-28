using UnityEngine;

public class AnimationStateSwitcher : MonoBehaviour
{
    private Animator animator;
    private float timer;
    private float nextTriggerTime;

    [Header("随机切换间隔（秒）")]
    public float minInterval = 2f;
    public float maxInterval = 5f;

    void Start()
    {
        animator = GetComponent<Animator>();
        if (animator == null)
        {
            Debug.LogError("未找到 Animator 组件！");
            enabled = false; // 禁用脚本，避免空引用
            return;
        }

        // 设置首次随机等待时间
        nextTriggerTime = Random.Range(minInterval, maxInterval);
        timer = 0f;
    }

    void Update()
    {
        // 安全校验
        if (animator == null) return;

        timer += Time.deltaTime;
        if (timer >= nextTriggerTime)
        {
            // 随机选择状态 0~3
            int randomState = Random.Range(0, 4);
            animator.SetInteger("AnimationState", randomState);

            // 日志输出当前触发状态（可根据需要删减）
            switch (randomState)
            {
                case 0: Debug.Log("随机触发 idle"); break;
                case 1: Debug.Log("随机触发 nod"); break;
                case 2: Debug.Log("随机触发 dismissing"); break;
                case 3: Debug.Log("随机触发 greeting"); break;
            }

            // 重置计时器并生成下次随机间隔
            timer = 0f;
            nextTriggerTime = Random.Range(minInterval, maxInterval);
        }
    }
}