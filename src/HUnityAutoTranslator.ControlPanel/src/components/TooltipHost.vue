<script setup lang="ts">
import { nextTick, onBeforeUnmount, onMounted, reactive, ref } from "vue";
import { placePopover } from "../utils/popover";

const tooltip = reactive({
  visible: false,
  placed: false,
  text: "",
  x: 0,
  y: 0
});
const tooltipRef = ref<HTMLElement | null>(null);
let currentTarget: HTMLElement | null = null;

function hide(): void {
  currentTarget = null;
  tooltip.visible = false;
  tooltip.placed = false;
}

function position(target: HTMLElement): void {
  if (currentTarget !== target || !tooltipRef.value) {
    return;
  }

  const { x, y } = placePopover(target, tooltipRef.value, { align: "left", gap: 8, margin: 10 });
  tooltip.x = x;
  tooltip.y = y;
  tooltip.placed = true;
}

function showFor(target: HTMLElement): void {
  const text = (target.getAttribute("data-help") ?? "").trim();
  if (!text) {
    hide();
    return;
  }

  currentTarget = target;
  tooltip.text = text;
  tooltip.visible = true;
  tooltip.placed = false;
  void nextTick(() => position(target));
}

function helpTarget(node: EventTarget | null): HTMLElement | null {
  return node instanceof Element ? node.closest<HTMLElement>("[data-help]") : null;
}

function onPointerOver(event: MouseEvent): void {
  const target = helpTarget(event.target);
  if (target === currentTarget) {
    return;
  }

  if (target) {
    showFor(target);
  } else {
    hide();
  }
}

function onPointerOut(event: MouseEvent): void {
  if (!event.relatedTarget) {
    hide();
  }
}

function onFocusIn(event: FocusEvent): void {
  const target = helpTarget(event.target);
  if (target) {
    showFor(target);
  } else {
    hide();
  }
}

onMounted(() => {
  document.addEventListener("mouseover", onPointerOver);
  document.addEventListener("mouseout", onPointerOut);
  document.addEventListener("focusin", onFocusIn);
  document.addEventListener("focusout", hide);
  window.addEventListener("scroll", hide, true);
  window.addEventListener("resize", hide);
});

onBeforeUnmount(() => {
  document.removeEventListener("mouseover", onPointerOver);
  document.removeEventListener("mouseout", onPointerOut);
  document.removeEventListener("focusin", onFocusIn);
  document.removeEventListener("focusout", hide);
  window.removeEventListener("scroll", hide, true);
  window.removeEventListener("resize", hide);
});
</script>

<template>
  <Teleport to="body">
    <div
      v-if="tooltip.visible"
      ref="tooltipRef"
      class="app-tooltip"
      role="tooltip"
      :style="{
        left: `${tooltip.x}px`,
        top: `${tooltip.y}px`,
        visibility: tooltip.placed ? 'visible' : 'hidden'
      }"
    >{{ tooltip.text }}</div>
  </Teleport>
</template>
