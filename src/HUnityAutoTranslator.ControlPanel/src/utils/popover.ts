export interface PopoverPosition {
  x: number;
  y: number;
}

export interface PlacePopoverOptions {
  /** Align the popover's left or right edge to the matching edge of the anchor. */
  align?: "left" | "right";
  /** Vertical gap between the anchor and the popover. */
  gap?: number;
  /** Minimum distance kept from the viewport edges. */
  margin?: number;
}

/**
 * Computes a viewport-clamped position for a fixed-position popover anchored to an
 * element. Prefers placing the popover below the anchor and only flips above when
 * it genuinely does not fit, using the popover's real measured size when available.
 */
export function placePopover(
  anchor: HTMLElement,
  menu: HTMLElement | null,
  options: PlacePopoverOptions = {}
): PopoverPosition {
  const align = options.align ?? "right";
  const gap = options.gap ?? 8;
  const margin = options.margin ?? 12;

  const anchorRect = anchor.getBoundingClientRect();
  const menuRect = menu?.getBoundingClientRect();
  const menuWidth = menuRect && menuRect.width > 0
    ? menuRect.width
    : Math.min(320, window.innerWidth - margin * 2);
  const menuHeight = menuRect && menuRect.height > 0
    ? menuRect.height
    : Math.min(360, window.innerHeight - margin * 2);

  const preferredLeft = align === "right"
    ? anchorRect.right - menuWidth
    : anchorRect.left;
  const x = Math.max(margin, Math.min(preferredLeft, window.innerWidth - menuWidth - margin));

  const belowTop = anchorRect.bottom + gap;
  const aboveTop = anchorRect.top - gap - menuHeight;
  let y: number;
  if (belowTop + menuHeight <= window.innerHeight - margin) {
    y = belowTop;
  } else if (aboveTop >= margin) {
    y = aboveTop;
  } else {
    y = Math.max(margin, window.innerHeight - menuHeight - margin);
  }

  return { x, y };
}
