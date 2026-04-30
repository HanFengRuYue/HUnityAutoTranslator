export interface LanguageOption {
  value: string;
  label: string;
}

export const targetLanguageOptions: LanguageOption[] = [
  { value: "zh-Hans", label: "简体中文" },
  { value: "zh-Hant", label: "繁体中文" },
  { value: "en", label: "英语" },
  { value: "ja", label: "日语" },
  { value: "ko", label: "韩语" },
  { value: "fr", label: "法语" },
  { value: "de", label: "德语" },
  { value: "es", label: "西班牙语" },
  { value: "pt", label: "葡萄牙语" },
  { value: "pt-BR", label: "巴西葡萄牙语" },
  { value: "ru", label: "俄语" },
  { value: "it", label: "意大利语" },
  { value: "th", label: "泰语" },
  { value: "vi", label: "越南语" },
  { value: "id", label: "印尼语" },
  { value: "tr", label: "土耳其语" },
  { value: "ar", label: "阿拉伯语" }
];

export function languageLabel(value: string | null | undefined): string {
  const code = (value ?? "").trim();
  if (!code) {
    return "";
  }

  return targetLanguageOptions.find((option) => option.value.toLowerCase() === code.toLowerCase())?.label ?? code;
}

export function normalizeLanguageInput(value: string | null | undefined): string {
  const raw = (value ?? "").trim();
  if (!raw) {
    return "";
  }

  const match = targetLanguageOptions.find((option) =>
    option.value.toLowerCase() === raw.toLowerCase() ||
    option.label.toLowerCase() === raw.toLowerCase());
  return match?.value ?? raw;
}

export function languageOptionsFor(value: string | null | undefined): LanguageOption[] {
  const code = normalizeLanguageInput(value);
  if (!code || targetLanguageOptions.some((option) => option.value === code)) {
    return targetLanguageOptions;
  }

  return [{ value: code, label: code }, ...targetLanguageOptions];
}
