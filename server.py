import asyncio
import websockets
import json
import numpy as np
import torch
import os
from funasr import AutoModel

# 加载 FunASR 模型
print("加载 FunASR 模型...")
# PICO Live Preview needs the GPU for video encoding. CPU inference keeps the
# headset connection stable and avoids competing with Ollama for VRAM.
device = os.environ.get("FUNASR_DEVICE", "cpu")
print(f"FunASR 推理设备: {device}")
model = AutoModel(
    model="paraformer-zh",
    device=device,
    disable_update=True,
    disable_progress=True
)
print("模型加载完成")

CAMPUS_HOTWORDS = (
    "苏州大学 未来校区 东吴门 教学中心 未来科创中心 国际创新中心 "
    "学术交流中心 科创文化连廊 智慧教室 科技展厅 "
    "人工智能 数据科学与大数据技术 机器人工程 机械电子工程"
)

async def handler(websocket):
    audio_buffer = bytearray()
    try:
        async for message in websocket:
            # 判断消息类型：如果是二进制，则是音频数据
            if isinstance(message, bytes):
                # 累积音频数据（假设客户端边录边发）
                audio_buffer.extend(message)
                # 可选：实时返回部分识别结果（需要流式模型支持）
                # 这里简单处理，不做实时识别
                continue

            # 否则按文本解析
            data = json.loads(message)
            cmd = data.get("type")
            if cmd == "start":
                audio_buffer = bytearray()  # 清空缓存
                await websocket.send(json.dumps({"type": "started"}))
                print("开始接收音频")
            elif cmd == "stop":
                if len(audio_buffer) == 0:
                    await websocket.send(json.dumps({"type": "error", "message": "No audio received"}))
                    continue
                print("停止接收，开始识别...")
                # 将音频数据转为 float32 数组
                audio_int16 = np.frombuffer(audio_buffer, dtype=np.int16)
                audio_float = audio_int16.astype(np.float32) / 32768.0
                # 调用 FunASR 识别
                res = model.generate(input=audio_float, hotword=CAMPUS_HOTWORDS)
                text = res[0].get('text', '').strip() if res else ""
                if text:
                    result = {"type": "final", "text": text}
                    await websocket.send(json.dumps(result))
                    print(f"识别结果: {text}")
                else:
                    await websocket.send(json.dumps({"type": "error", "message": "No speech recognized"}))
            else:
                await websocket.send(json.dumps({"type": "error", "message": "Unknown command"}))
    except websockets.exceptions.ConnectionClosed:
        print("客户端断开连接")
    finally:
        # 清理资源
        pass

async def main():
    host = os.environ.get("FUNASR_HOST", "0.0.0.0")
    port = int(os.environ.get("FUNASR_PORT", "8765"))
    async with websockets.serve(handler, host, port):
        print(f"WebSocket ASR 服务已启动 ws://{host}:{port}")
        await asyncio.Future()

if __name__ == "__main__":
    asyncio.run(main())
