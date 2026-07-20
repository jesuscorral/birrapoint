import { Directive, HostListener, output } from '@angular/core';

// T048B: CDK drag already lets a mousedown+move sequence become a drag before any native click
// fires, but relying on the browser's own click-suppression-after-drag is unreliable across
// pointer/preview setups. This disambiguates independently of CDK: a pointerdown/pointerup pair
// whose total movement stays within the threshold is a click; anything past it is left for CDK's
// own drag handling to interpret, so this never calls preventDefault/stopPropagation.
export const CLICK_MOVEMENT_THRESHOLD_PX = 6;

export function isWithinClickThreshold(
  dx: number,
  dy: number,
  thresholdPx: number = CLICK_MOVEMENT_THRESHOLD_PX,
): boolean {
  return Math.hypot(dx, dy) <= thresholdPx;
}

@Directive({
  selector: '[appClickVsDrag]',
  standalone: true,
})
export class ClickVsDragDirective {
  readonly appClickVsDrag = output<void>();

  @HostListener('pointerdown', ['$event'])
  onPointerDown(event: PointerEvent): void {
    const startX = event.clientX;
    const startY = event.clientY;

    const onPointerUp = (upEvent: PointerEvent): void => {
      window.removeEventListener('pointerup', onPointerUp);
      if (isWithinClickThreshold(upEvent.clientX - startX, upEvent.clientY - startY)) {
        this.appClickVsDrag.emit();
      }
    };

    window.addEventListener('pointerup', onPointerUp);
  }

  @HostListener('keydown.enter')
  onEnterActivate(): void {
    this.appClickVsDrag.emit();
  }

  @HostListener('keydown.space', ['$event'])
  onSpaceActivate(event: Event): void {
    // Prevent the default page-scroll a focused non-form element would otherwise get from Space.
    event.preventDefault();
    this.appClickVsDrag.emit();
  }
}
