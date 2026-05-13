import { invokeToolbox } from "../bridge";
import { setLastError, showToast } from "../state/toolboxStore";

interface InvokeOptions {
  silent?: boolean;
  successMessage?: string;
}

export async function safeInvoke<T>(command: string, payload: unknown = {}, options: InvokeOptions = {}): Promise<T | null> {
  try {
    const result = await invokeToolbox<T>(command, payload);
    setLastError(null);
    if (options.successMessage) {
      showToast(options.successMessage, "ok");
    }
    return result;
  } catch (error) {
    setLastError(error);
    if (!options.silent) {
      const message = error instanceof Error ? error.message : "工具箱命令失败";
      showToast(message, "error");
    }
    return null;
  }
}
