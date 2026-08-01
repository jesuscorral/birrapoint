import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'bp-button',
  standalone: true,
  imports: [CommonModule],
  template: `
    <button
      [class]="buttonClasses"
      [type]="type"
      [disabled]="loading || disabled"
      (click)="onClick.emit()"
      [attr.aria-busy]="loading"
    >
      <span *ngIf="loading" class="spinner" aria-hidden="true"></span>
      <span *ngIf="!loading && icon" class="mr-2" [innerHTML]="icon"></span>
      {{ label }}
    </button>
  `,
  styles: [
    `
      .spinner {
        display: inline-block;
        width: 16px;
        height: 16px;
        border-radius: 50%;
        border: 2px solid currentColor;
        border-top-color: transparent;
        opacity: 0.75;
        animation: spin 0.7s linear infinite;
        margin-right: 0.5rem;
      }

      @keyframes spin {
        to {
          transform: rotate(360deg);
        }
      }

      @media (prefers-reduced-motion: reduce) {
        .spinner {
          animation-duration: 2s;
        }
      }

      .mr-2 {
        margin-right: 0.5rem;
      }
    `,
  ],
})
export class BpButtonComponent {
  @Input() label = '';
  @Input() variant: 'primary' | 'secondary' | 'ghost' = 'primary';
  @Input() size: 'sm' | 'md' | 'lg' = 'md';
  @Input() type: 'button' | 'submit' | 'reset' = 'button';
  @Input() loading = false;
  @Input() disabled = false;
  @Input() block = false;
  @Input() icon: string | null = null;
  @Output() onClick = new EventEmitter<void>();

  get buttonClasses(): string {
    const base =
      'inline-flex items-center justify-center gap-2 font-semibold rounded-md cursor-pointer transition-all duration-150 ease-out focus-visible:outline-none focus-visible:ring-3 focus-visible:ring-offset-2';
    const variant = this.variantClasses();
    const sizeClass = this.sizeClass();
    const blockClass = this.block ? 'w-full' : '';
    const disabledClass = this.loading || this.disabled ? 'opacity-50 pointer-events-none' : '';

    return `${base} ${variant} ${sizeClass} ${blockClass} ${disabledClass}`.trim();
  }

  private variantClasses(): string {
    switch (this.variant) {
      case 'primary':
        return 'min-h-11 px-5 bg-bp-cobre-500 text-white border-1.5 border-bp-cobre-500 hover:bg-bp-cobre-600 hover:border-bp-cobre-600 active:bg-bp-cobre-700 active:border-bp-cobre-700 active:translate-y-0.5 focus-visible:ring-bp-hueso-50 focus-visible:ring-offset-bp-cobre-500';
      case 'secondary':
        return 'min-h-11 px-5 bg-bp-surface text-bp-verde-600 border-1.5 border-bp-border-strong hover:bg-bp-verde-50 hover:border-bp-verde-400 focus-visible:ring-bp-hueso-50 focus-visible:ring-offset-bp-cobre-500';
      case 'ghost':
        return 'px-3 text-bp-text-muted hover:bg-bp-hueso-100 hover:text-bp-text focus-visible:ring-bp-hueso-50 focus-visible:ring-offset-bp-surface';
      default:
        return '';
    }
  }

  private sizeClass(): string {
    switch (this.size) {
      case 'sm':
        return 'min-h-9 px-3 text-sm';
      case 'lg':
        return 'min-h-13 px-6 text-lg';
      case 'md':
      default:
        return 'min-h-11 px-5 text-base';
    }
  }
}
