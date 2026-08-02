import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

@Component({
  selector: 'bp-button',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button
      [class]="buttonClasses()"
      [type]="type()"
      [disabled]="loading() || disabled()"
      (click)="clicked.emit()"
      [attr.aria-busy]="loading()"
      [attr.aria-label]="ariaLabel()"
    >
      @if (loading()) {
        <span class="spinner" aria-hidden="true"></span>
      }
      {{ label() }}
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
    `,
  ],
})
export class BpButtonComponent {
  readonly label = input('');
  // Overrides the accessible name with something more specific than the visible label — needed
  // whenever several buttons sharing the same label (e.g. one "Excluir" per list row) are on
  // screen at once, so assistive tech can distinguish them. `null` (the default) leaves the
  // native button's implicit accessible name (its text content) untouched.
  readonly ariaLabel = input<string | null>(null);
  readonly variant = input<'primary' | 'secondary' | 'ghost'>('primary');
  readonly size = input<'sm' | 'md' | 'lg'>('md');
  readonly type = input<'button' | 'submit' | 'reset'>('button');
  readonly loading = input(false);
  readonly disabled = input(false);
  readonly block = input(false);
  readonly clicked = output<void>();

  protected readonly buttonClasses = computed(() => {
    const base =
      'inline-flex items-center justify-center gap-2 font-semibold rounded-md cursor-pointer transition-all duration-150 ease-out focus-visible:outline-none focus-visible:ring-3 focus-visible:ring-offset-2';
    const variant = this.variantClasses();
    const sizeClass = this.sizeClass();
    const blockClass = this.block() ? 'w-full' : '';
    const disabledClass = this.loading() || this.disabled() ? 'opacity-50 pointer-events-none' : '';

    return `${base} ${variant} ${sizeClass} ${blockClass} ${disabledClass}`.trim();
  });

  private variantClasses(): string {
    switch (this.variant()) {
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
    switch (this.size()) {
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
