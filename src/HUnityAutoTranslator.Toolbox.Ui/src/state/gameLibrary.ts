import { safeInvoke } from "../api/client";
import { toolboxStore } from "./toolboxStore";
import type { GameInspection, GameLibraryEntry } from "../types/api";

export function rootDisplayName(root: string): string {
  return root.split(/[\\/]/).filter(Boolean).pop() || "Unity 游戏";
}

export function createGameId(root: string): string {
  const normalized = root.trim().toLowerCase();
  let hash = 0;
  for (let index = 0; index < normalized.length; index += 1) {
    hash = ((hash << 5) - hash + normalized.charCodeAt(index)) | 0;
  }
  return `game-${Math.abs(hash)}`;
}

export function buildEntry(root: string, inspection: GameInspection | null): GameLibraryEntry {
  const normalizedRoot = inspection?.GameRoot || root.trim();
  const id = createGameId(normalizedRoot);
  const now = new Date().toISOString();
  const existing = toolboxStore.games.find((game) => game.Id === id);
  return {
    Id: id,
    Root: normalizedRoot,
    Name: inspection?.GameName?.trim() || existing?.Name || rootDisplayName(normalizedRoot),
    AddedUtc: existing?.AddedUtc ?? now,
    UpdatedUtc: now,
    Inspection: inspection
  };
}

export async function inspectRoot(root: string): Promise<GameInspection | null> {
  const trimmed = root.trim();
  if (!trimmed) {
    return null;
  }

  toolboxStore.isInspecting = true;
  try {
    return await safeInvoke<GameInspection>("inspectGame", { gameRoot: trimmed }, { silent: true });
  } finally {
    toolboxStore.isInspecting = false;
  }
}
