export const themeStorageKey = "hunity.toolbox.theme";
export const sidebarStorageKey = "hunity.toolbox.sidebarCollapsed";
export const gameLibraryStorageKey = "hunity.toolbox.gameLibrary";
export const selectedGameStorageKey = "hunity.toolbox.selectedGame";
export const libraryLayoutStorageKey = "hunity.toolbox.libraryLayout";
export const libraryPosterSizeStorageKey = "hunity.toolbox.libraryPosterSize";
export const libraryAccentStorageKey = "hunity.toolbox.libraryAccent";

export function readStoredString(key: string): string {
  try {
    return window.localStorage.getItem(key) ?? "";
  } catch {
    return "";
  }
}

export function writeStoredString(key: string, value: string): void {
  try {
    if (value) {
      window.localStorage.setItem(key, value);
    } else {
      window.localStorage.setItem(key, "");
    }
  } catch {
    // WebView2 NavigateToString can reject storage access before Vue mounts.
  }
}

export function readStoredBool(key: string, fallback: boolean): boolean {
  const saved = readStoredString(key);
  return saved === "true" ? true : saved === "false" ? false : fallback;
}

export function readStoredOption<T extends string>(key: string, allowed: readonly T[], fallback: T): T {
  const saved = readStoredString(key);
  return allowed.includes(saved as T) ? saved as T : fallback;
}
