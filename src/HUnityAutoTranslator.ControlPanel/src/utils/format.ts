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

function padDatePart(value: number): string {
  return String(value).padStart(2, "0");
}

export function formatFullDateTime(value: string | null | undefined): string {
  if (!value) {
    return "-";
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  const year = date.getFullYear();
  const month = padDatePart(date.getMonth() + 1);
  const day = padDatePart(date.getDate());
  const hour = padDatePart(date.getHours());
  const minute = padDatePart(date.getMinutes());
  const second = padDatePart(date.getSeconds());
  return `${year}-${month}-${day} ${hour}:${minute}:${second}`;
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
