export function formatNumber(value: number | null | undefined): string {
  return new Intl.NumberFormat("zh-CN").format(value ?? 0);
}

export function formatMilliseconds(value: number | null | undefined): string {
  const milliseconds = Math.max(0, Math.round(value ?? 0));
  if (milliseconds >= 1000) {
    return `${(milliseconds / 1000).toFixed(1)} 秒`;
  }

  return `${milliseconds} 毫秒`;
}

export function formatRate(value: number | null | undefined): string {
  return `${Math.max(0, value ?? 0).toFixed(1)} 字/秒`;
}

export function formatDateTime(value: string | null | undefined): string {
  if (!value) {
    return "-";
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return new Intl.DateTimeFormat("zh-CN", {
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit"
  }).format(date);
}
