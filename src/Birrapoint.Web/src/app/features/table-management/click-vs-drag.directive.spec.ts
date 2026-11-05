import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import {
  CLICK_MOVEMENT_THRESHOLD_PX,
  ClickVsDragDirective,
  isWithinClickThreshold,
} from './click-vs-drag.directive';

describe('isWithinClickThreshold', () => {
  it('treats zero movement as a click', () => {
    expect(isWithinClickThreshold(0, 0)).toBe(true);
  });

  it('treats movement at the default threshold as a click', () => {
    expect(isWithinClickThreshold(CLICK_MOVEMENT_THRESHOLD_PX, 0)).toBe(true);
  });

  it('treats movement past the default threshold as a drag, not a click', () => {
    expect(isWithinClickThreshold(CLICK_MOVEMENT_THRESHOLD_PX + 1, 0)).toBe(false);
  });

  it('measures Euclidean distance across both axes', () => {
    // 3-4-5 triangle: comfortably past a threshold of 4 on either axis alone.
    expect(isWithinClickThreshold(3, 4, 4)).toBe(false);
    expect(isWithinClickThreshold(3, 4, 5)).toBe(true);
  });
});

@Component({
  standalone: true,
  imports: [ClickVsDragDirective],
  template: `<div appClickVsDrag (appClickVsDrag)="onClick()" tabindex="0"></div>`,
})
class HostComponent {
  clicks = 0;
  onClick(): void {
    this.clicks++;
  }
}

// jsdom has no PointerEvent constructor; MouseEvent carries the clientX/clientY the directive
// reads and accepts an arbitrary event type, so it stands in for pointerdown/pointerup here.
function pointerEvent(type: string, x: number, y: number): MouseEvent {
  return new MouseEvent(type, { clientX: x, clientY: y });
}

describe('ClickVsDragDirective', () => {
  function createHost() {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    const element = fixture.nativeElement.querySelector('div') as HTMLDivElement;
    return { fixture, element };
  }

  it('emits appClickVsDrag when pointerdown/pointerup happen with negligible movement', () => {
    const { fixture, element } = createHost();

    element.dispatchEvent(pointerEvent('pointerdown', 100, 100));
    window.dispatchEvent(pointerEvent('pointerup', 102, 101));

    expect(fixture.componentInstance.clicks).toBe(1);
  });

  it('does not emit when the pointer moves past the click threshold before release', () => {
    const { fixture, element } = createHost();

    element.dispatchEvent(pointerEvent('pointerdown', 100, 100));
    window.dispatchEvent(pointerEvent('pointerup', 140, 100));

    expect(fixture.componentInstance.clicks).toBe(0);
  });

  it('emits on Enter keydown', () => {
    const { fixture, element } = createHost();

    element.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));

    expect(fixture.componentInstance.clicks).toBe(1);
  });

  it('emits on Space keydown and prevents the default scroll', () => {
    const { fixture, element } = createHost();
    const event = new KeyboardEvent('keydown', { key: ' ', cancelable: true });

    element.dispatchEvent(event);

    expect(fixture.componentInstance.clicks).toBe(1);
    expect(event.defaultPrevented).toBe(true);
  });
});
