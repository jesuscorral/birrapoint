import { Location } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';

import { CompetitionsApiService } from '../../core/api/competitions-api.service';
import type { CompetitionDetail } from '../../core/api/competitions-api.service';
import { BasicsStepComponent } from './steps/basics-step.component';
import { DetailsStepComponent } from './steps/details-step.component';

@Component({
  selector: 'app-competition-wizard',
  imports: [BasicsStepComponent, DetailsStepComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (loading()) {
      <p>Loading…</p>
    } @else if (loadError()) {
      <p role="alert">Unable to load this competition.</p>
    } @else {
      @switch (currentStep()) {
        @case (1) {
          <app-basics-step
            [competitionId]="competitionId()"
            [initialValue]="competition()"
            (saved)="onBasicsSaved($event)"
          />
        }
        @case (2) {
          <app-details-step
            [competitionId]="competitionId()!"
            [initialValue]="competition()"
            (saved)="onDetailsSaved($event)"
          />
        }
      }
    }
  `,
})
export class CompetitionWizardComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly location = inject(Location);
  private readonly api = inject(CompetitionsApiService);

  protected readonly currentStep = signal<1 | 2>(1);
  protected readonly competitionId = signal<string | null>(null);
  protected readonly competition = signal<CompetitionDetail | null>(null);
  protected readonly loading = signal(false);
  protected readonly loadError = signal(false);

  constructor() {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.competitionId.set(id);
      this.loading.set(true);
      this.api.getById(id).subscribe({
        next: (detail) => {
          this.competition.set(detail);
          this.loading.set(false);
        },
        error: () => {
          this.loading.set(false);
          this.loadError.set(true);
        },
      });
    }
  }

  protected onBasicsSaved(detail: CompetitionDetail): void {
    const isNew = this.competitionId() === null;
    this.competitionId.set(detail.id);
    this.competition.set(detail);
    if (isNew) {
      // Location.replaceState only swaps the address bar/history entry, not the Router's active
      // route — a router.navigate here would recreate this component (different Route config for
      // /new vs /:id) and lose currentStep/competition state. This still satisfies "a reload lands
      // back on the same wizard" since a fresh page load reads the real browser URL.
      this.location.replaceState(`/organizer/competitions/${detail.id}`);
    }
    this.currentStep.set(2);
  }

  protected onDetailsSaved(detail: CompetitionDetail): void {
    this.competition.set(detail);
  }
}
