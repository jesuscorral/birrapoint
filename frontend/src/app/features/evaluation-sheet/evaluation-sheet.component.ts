import {
  ChangeDetectionStrategy,
  Component,
  OnDestroy,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import type { Subscription } from 'rxjs';
import { filter } from 'rxjs';

import { ApiError } from '../../core/api/api-error';
import type {
  EvaluationComments,
  EvaluationDescriptors,
  EvaluationScores,
} from '../../core/offline/db';
import { SyncService } from '../../core/offline/sync.service';
import { CompetitionHubService } from '../../core/realtime/competition-hub.service';
import type { JudgeRemovedEvent } from '../../core/realtime/competition-hub.events';
import { BpPageShellComponent } from '../../core/layout/bp-page-shell/bp-page-shell.component';
import { BpInputComponent } from '../../shared/components/bp-input/bp-input.component';
import { BpStepActionsComponent } from '../../shared/components/bp-step-actions/bp-step-actions.component';
import { BpTextareaComponent } from '../../shared/components/bp-textarea/bp-textarea.component';
import { BpBipolarSliderComponent } from './descriptor-controls/bp-bipolar-slider.component';
import { BpDiscreteSliderComponent } from './descriptor-controls/bp-discrete-slider.component';
import {
  CLARITY_OPTIONS,
  COLOR_OPTIONS,
  FOAM_OPTIONS,
  OFF_FLAVOR_OPTIONS,
} from './evaluation-descriptor-catalog';
import { StyleReferencePanelComponent } from './style-reference/style-reference-panel.component';
import { TastingOrderApiService } from '../judge-tables/tasting-order-api.service';
import type { JudgeSample, JudgeTableSummary } from '../judge-tables/tasting-order-api.service';

function toGenericApiError(error: unknown): ApiError {
  return error instanceof ApiError
    ? error
    : new ApiError({ status: 0, title: 'Ha ocurrido un error inesperado.', urn: null });
}

function errorMessage(error: ApiError): string {
  return error.detail ?? error.title;
}

// Best-effort cache of the last successfully-fetched sample per beerEntryId (US7 Acceptance
// Scenario 3/quickstart.md scenario 7: "restart the app [while offline], verify data intact").
// Deliberately a plain localStorage entry, not a Dexie table: this is read-only metadata
// (blindCode/style/evaluationStatus) refreshed on every successful mount, not something the
// offline engine (R-08, SyncService) needs to reconcile or replay — it only ever gets consulted
// as a fallback when the live fetch below can't reach the server at all.
const SAMPLE_CACHE_KEY_PREFIX = 'birrapoint:evaluation-sample:';

function cacheSample(sample: JudgeSample): void {
  try {
    localStorage.setItem(`${SAMPLE_CACHE_KEY_PREFIX}${sample.beerEntryId}`, JSON.stringify(sample));
  } catch {
    // Best-effort: a full/blocked localStorage only forfeits the offline-restart fallback below,
    // never today's live (online) rendering path.
  }
}

function readCachedSample(beerEntryId: string): JudgeSample | null {
  try {
    const raw = localStorage.getItem(`${SAMPLE_CACHE_KEY_PREFIX}${beerEntryId}`);
    return raw ? (JSON.parse(raw) as JudgeSample) : null;
  } catch {
    return null;
  }
}

// FR-023/FR-025 section caps and minimum comment length — must match SubmitEvaluationRules on
// the backend exactly (client-side enforcement here is defense-in-depth, not a replacement: the
// backend re-validates every submission regardless).
const MIN_COMMENT_LENGTH = 20;

type SectionKey = keyof EvaluationScores;

interface EvaluationSectionConfig {
  key: SectionKey;
  label: string;
  max: number;
  scoreControl:
    'aromaScore' | 'appearanceScore' | 'flavorScore' | 'mouthfeelScore' | 'overallScore';
  commentControl:
    'aromaComment' | 'appearanceComment' | 'flavorComment' | 'mouthfeelComment' | 'overallComment';
}

// Order matches the reference paper sheet (VIII Concurso Homebrewer Córdoba, Session 2026-09-21):
// Apariencia, Aroma, Sabor, Sensación en boca, Impresión general — not the classic BJCP form
// order (Aroma first), deliberately, to mirror what the organizer supplied.
const SECTIONS: EvaluationSectionConfig[] = [
  {
    key: 'appearance',
    label: 'Apariencia',
    max: 3,
    scoreControl: 'appearanceScore',
    commentControl: 'appearanceComment',
  },
  {
    key: 'aroma',
    label: 'Aroma',
    max: 12,
    scoreControl: 'aromaScore',
    commentControl: 'aromaComment',
  },
  {
    key: 'flavor',
    label: 'Sabor',
    max: 20,
    scoreControl: 'flavorScore',
    commentControl: 'flavorComment',
  },
  {
    key: 'mouthfeel',
    label: 'Sensación en boca',
    max: 5,
    scoreControl: 'mouthfeelScore',
    commentControl: 'mouthfeelComment',
  },
  {
    key: 'overall',
    label: 'Impresión general',
    max: 10,
    scoreControl: 'overallScore',
    commentControl: 'overallComment',
  },
];

type ActiveTab = SectionKey | 'summary';

function buildForm(): FormGroup {
  const group: Record<string, FormControl> = {};
  for (const section of SECTIONS) {
    group[section.scoreControl] = new FormControl<number | null>(null, {
      validators: [Validators.required, Validators.min(0), Validators.max(section.max)],
    });
    group[section.commentControl] = new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.minLength(MIN_COMMENT_LENGTH)],
    });
  }
  // Session 2026-09-21 (FR-063): holistic free-text feedback, distinct from the five section
  // comments above — optional, no minimum length.
  group['feedback'] = new FormControl('', { nonNullable: true });
  return new FormGroup(group);
}

// Session 2026-09-21 (FR-063): the structured tasting-sheet descriptor working state. Plain
// signal, not part of the reactive FormGroup above — every field here is optional/advisory (never
// gates submit), so it doesn't need Validators/FormControl machinery. Text/select/checkbox fields
// default to '' / false, which already mean "not filled" unambiguously. Slider fields are the one
// case that needs care: a slider has no natural "empty" position, so its state field defaults to
// `null` — genuinely untouched — and the *template* alone supplies a display-only fallback
// (`?? 0` / `?? 50`) for where the handle sits before the judge ever drags it. `null` is what
// actually reaches the submit payload and Dexie draft (senior-review B1: an earlier version of
// this file defaulted the state fields themselves to 0/50, so an untouched slider silently
// persisted a fabricated rating — visible in the organizer's audit view and the participant-facing
// results PDF as if the judge had deliberately rated it "Nada"/neutral. Fixed by moving the
// default out of the state and into the template's own display binding.)
interface AppearanceDescriptorsState {
  color: string;
  colorOther: string;
  colorInappropriate: boolean;
  clarity: string;
  clarityInappropriate: boolean;
  foam: string;
  foamOther: string;
  foamInappropriate: boolean;
  // null until the judge actually drags the slider (see toDescriptorsPayload/senior-review B1):
  // the template falls back to a display-only default (?? 50/?? 0), but the stored/submitted
  // value stays null — and is therefore omitted from the payload — until touched.
  retention: number | null;
  retentionInappropriate: boolean;
  texture: string;
  notes: string;
}

interface AromaDescriptorsState {
  malt: number | null;
  maltInappropriate: boolean;
  hops: number | null;
  hopsInappropriate: boolean;
  fermentation: number | null;
  fermentationInappropriate: boolean;
}

interface FlavorDescriptorsState {
  malt: number | null;
  maltInappropriate: boolean;
  hops: number | null;
  hopsInappropriate: boolean;
  bitterness: number | null;
  bitternessInappropriate: boolean;
  fermentation: number | null;
  fermentationInappropriate: boolean;
  balance: number | null;
  balanceInappropriate: boolean;
  finish: number | null;
  finishInappropriate: boolean;
}

interface MouthfeelDescriptorsState {
  body: number | null;
  bodyInappropriate: boolean;
  carbonation: number | null;
  carbonationInappropriate: boolean;
  alcoholWarmth: number | null;
  alcoholWarmthInappropriate: boolean;
  creaminess: number | null;
  creaminessInappropriate: boolean;
  astringency: number | null;
  astringencyInappropriate: boolean;
  notes: string;
}

interface OverallDescriptorsState {
  classicExample: number | null;
  classicExampleInappropriate: boolean;
  defects: number | null;
  defectsInappropriate: boolean;
  vitality: number | null;
  vitalityInappropriate: boolean;
}

interface DescriptorsFormState {
  appearance: AppearanceDescriptorsState;
  aroma: AromaDescriptorsState;
  flavor: FlavorDescriptorsState;
  mouthfeel: MouthfeelDescriptorsState;
  overall: OverallDescriptorsState;
  offFlavors: Set<string>;
}

function initialDescriptorsState(): DescriptorsFormState {
  return {
    appearance: {
      color: '',
      colorOther: '',
      colorInappropriate: false,
      clarity: '',
      clarityInappropriate: false,
      foam: '',
      foamOther: '',
      foamInappropriate: false,
      retention: null,
      retentionInappropriate: false,
      texture: '',
      notes: '',
    },
    aroma: {
      malt: null,
      maltInappropriate: false,
      hops: null,
      hopsInappropriate: false,
      fermentation: null,
      fermentationInappropriate: false,
    },
    flavor: {
      malt: null,
      maltInappropriate: false,
      hops: null,
      hopsInappropriate: false,
      bitterness: null,
      bitternessInappropriate: false,
      fermentation: null,
      fermentationInappropriate: false,
      balance: null,
      balanceInappropriate: false,
      finish: null,
      finishInappropriate: false,
    },
    mouthfeel: {
      body: null,
      bodyInappropriate: false,
      carbonation: null,
      carbonationInappropriate: false,
      alcoholWarmth: null,
      alcoholWarmthInappropriate: false,
      creaminess: null,
      creaminessInappropriate: false,
      astringency: null,
      astringencyInappropriate: false,
      notes: '',
    },
    overall: {
      classicExample: null,
      classicExampleInappropriate: false,
      defects: null,
      defectsInappropriate: false,
      vitality: null,
      vitalityInappropriate: false,
    },
    offFlavors: new Set<string>(),
  };
}

// Converts the working state above into the wire/Dexie shape (EvaluationDescriptors) — empty
// strings become undefined so an untouched select/text field reads as genuinely absent rather
// than an empty-string value the backend's closed-list validator would reject. Numeric slider
// fields need no such conversion: they're already `null` in the state until the judge actually
// touches the control (senior-review B1 fix — a judge who never drags a slider must not have a
// fabricated "Nada"/midpoint rating land in the organizer's audit view and the participant-facing
// results PDF as if they'd deliberately rated it).
function toDescriptorsPayload(state: DescriptorsFormState): EvaluationDescriptors {
  const orUndefined = (value: string): string | undefined => (value === '' ? undefined : value);
  return {
    appearance: {
      color: orUndefined(state.appearance.color),
      colorOther: orUndefined(state.appearance.colorOther),
      colorInappropriate: state.appearance.colorInappropriate,
      clarity: orUndefined(state.appearance.clarity),
      clarityInappropriate: state.appearance.clarityInappropriate,
      foam: orUndefined(state.appearance.foam),
      foamOther: orUndefined(state.appearance.foamOther),
      foamInappropriate: state.appearance.foamInappropriate,
      retention: state.appearance.retention,
      retentionInappropriate: state.appearance.retentionInappropriate,
      texture: orUndefined(state.appearance.texture),
      notes: orUndefined(state.appearance.notes),
    },
    aroma: { ...state.aroma },
    flavor: { ...state.flavor },
    mouthfeel: { ...state.mouthfeel },
    overall: { ...state.overall },
    offFlavors: [...state.offFlavors],
  };
}

// The inverse, used to hydrate an existing offline draft (initialize() below) — tolerant of a
// partially-absent EvaluationDescriptors (an older draft saved before a given field existed, or
// one where a whole section was never filled) by falling back to the same defaults as
// initialDescriptorsState(). Field-by-field coalescing (not a blind object spread) because
// EvaluationDescriptors' fields are `| null | undefined` (the wire/Dexie shape) while
// DescriptorsFormState's are concrete defaulted values (this file's own working-state shape) —
// spreading a possibly-null/undefined field over a concrete default would let null/undefined
// leak through untyped.
function fromDescriptorsPayload(payload: EvaluationDescriptors | undefined): DescriptorsFormState {
  const base = initialDescriptorsState();
  if (!payload) {
    return base;
  }
  return {
    appearance: {
      color: payload.appearance?.color ?? base.appearance.color,
      colorOther: payload.appearance?.colorOther ?? base.appearance.colorOther,
      colorInappropriate:
        payload.appearance?.colorInappropriate ?? base.appearance.colorInappropriate,
      clarity: payload.appearance?.clarity ?? base.appearance.clarity,
      clarityInappropriate:
        payload.appearance?.clarityInappropriate ?? base.appearance.clarityInappropriate,
      foam: payload.appearance?.foam ?? base.appearance.foam,
      foamOther: payload.appearance?.foamOther ?? base.appearance.foamOther,
      foamInappropriate: payload.appearance?.foamInappropriate ?? base.appearance.foamInappropriate,
      retention: payload.appearance?.retention ?? base.appearance.retention,
      retentionInappropriate:
        payload.appearance?.retentionInappropriate ?? base.appearance.retentionInappropriate,
      texture: payload.appearance?.texture ?? base.appearance.texture,
      notes: payload.appearance?.notes ?? base.appearance.notes,
    },
    aroma: {
      malt: payload.aroma?.malt ?? base.aroma.malt,
      maltInappropriate: payload.aroma?.maltInappropriate ?? base.aroma.maltInappropriate,
      hops: payload.aroma?.hops ?? base.aroma.hops,
      hopsInappropriate: payload.aroma?.hopsInappropriate ?? base.aroma.hopsInappropriate,
      fermentation: payload.aroma?.fermentation ?? base.aroma.fermentation,
      fermentationInappropriate:
        payload.aroma?.fermentationInappropriate ?? base.aroma.fermentationInappropriate,
    },
    flavor: {
      malt: payload.flavor?.malt ?? base.flavor.malt,
      maltInappropriate: payload.flavor?.maltInappropriate ?? base.flavor.maltInappropriate,
      hops: payload.flavor?.hops ?? base.flavor.hops,
      hopsInappropriate: payload.flavor?.hopsInappropriate ?? base.flavor.hopsInappropriate,
      bitterness: payload.flavor?.bitterness ?? base.flavor.bitterness,
      bitternessInappropriate:
        payload.flavor?.bitternessInappropriate ?? base.flavor.bitternessInappropriate,
      fermentation: payload.flavor?.fermentation ?? base.flavor.fermentation,
      fermentationInappropriate:
        payload.flavor?.fermentationInappropriate ?? base.flavor.fermentationInappropriate,
      balance: payload.flavor?.balance ?? base.flavor.balance,
      balanceInappropriate:
        payload.flavor?.balanceInappropriate ?? base.flavor.balanceInappropriate,
      finish: payload.flavor?.finish ?? base.flavor.finish,
      finishInappropriate: payload.flavor?.finishInappropriate ?? base.flavor.finishInappropriate,
    },
    mouthfeel: {
      body: payload.mouthfeel?.body ?? base.mouthfeel.body,
      bodyInappropriate: payload.mouthfeel?.bodyInappropriate ?? base.mouthfeel.bodyInappropriate,
      carbonation: payload.mouthfeel?.carbonation ?? base.mouthfeel.carbonation,
      carbonationInappropriate:
        payload.mouthfeel?.carbonationInappropriate ?? base.mouthfeel.carbonationInappropriate,
      alcoholWarmth: payload.mouthfeel?.alcoholWarmth ?? base.mouthfeel.alcoholWarmth,
      alcoholWarmthInappropriate:
        payload.mouthfeel?.alcoholWarmthInappropriate ?? base.mouthfeel.alcoholWarmthInappropriate,
      creaminess: payload.mouthfeel?.creaminess ?? base.mouthfeel.creaminess,
      creaminessInappropriate:
        payload.mouthfeel?.creaminessInappropriate ?? base.mouthfeel.creaminessInappropriate,
      astringency: payload.mouthfeel?.astringency ?? base.mouthfeel.astringency,
      astringencyInappropriate:
        payload.mouthfeel?.astringencyInappropriate ?? base.mouthfeel.astringencyInappropriate,
      notes: payload.mouthfeel?.notes ?? base.mouthfeel.notes,
    },
    overall: {
      classicExample: payload.overall?.classicExample ?? base.overall.classicExample,
      classicExampleInappropriate:
        payload.overall?.classicExampleInappropriate ?? base.overall.classicExampleInappropriate,
      defects: payload.overall?.defects ?? base.overall.defects,
      defectsInappropriate:
        payload.overall?.defectsInappropriate ?? base.overall.defectsInappropriate,
      vitality: payload.overall?.vitality ?? base.overall.vitality,
      vitalityInappropriate:
        payload.overall?.vitalityInappropriate ?? base.overall.vitalityInappropriate,
    },
    offFlavors: new Set(payload.offFlavors ?? []),
  };
}

// T059/US7: the offline-first blind evaluation sheet (FR-022/FR-023/FR-025/FR-026/FR-027,
// SC-003). Judge-facing — only ever renders/handles blindCode + style (BR-01/FR-019); never an
// entrant field. All persistence (drafts, outbox, replay) is delegated to SyncService — this
// component never touches Dexie or the network directly.
//
// Session 2026-09-21: redesigned to mirror a reference paper BJCP-style score sheet — a section
// nav bar (freely jump between Apariencia/Aroma/Sabor/Sensación en boca/Impresión general/Resumen
// at any time to revise an earlier section, not a gated linear wizard) plus structured tasting
// descriptors per section (FR-063: discrete 4-stop intensity sliders, continuous bipolar sliders,
// closed-choice selects, a 20-term off-flavor checklist, holistic Feedback). The FormGroup/
// validators/draft/submit logic for the five scores/comments is unchanged; descriptors are a
// separate, non-validated signal (see DescriptorsFormState's own doc comment) merged into the
// same submit payload.
@Component({
  selector: 'app-evaluation-sheet',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    StyleReferencePanelComponent,
    BpPageShellComponent,
    BpInputComponent,
    BpTextareaComponent,
    BpStepActionsComponent,
    BpDiscreteSliderComponent,
    BpBipolarSliderComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <bp-page-shell homeLink="/judge/tables">
      <p><a [routerLink]="['/judge', 'tables', tableId]">&larr; Volver a la mesa</a></p>

      @if (isOffline()) {
        <p role="status" class="offline-badge">Modo sin conexión — datos protegidos localmente</p>
      }

      @if (loadError(); as message) {
        <p role="alert">{{ message }}</p>
      }

      @if (!loadError() && sample(); as currentSample) {
        <div class="evaluation-sheet-header">
          <h1>{{ currentSample.blindCode }}</h1>
          <p class="sample-style">
            {{ currentSample.styleName }} ({{ currentSample.styleCode }}) ·
            {{ currentSample.abvPercent }}% ABV
          </p>

          <app-style-reference-panel
            [styleCode]="currentSample.styleCode"
            [styleName]="currentSample.styleName"
          />
        </div>

        @if (currentSample.evaluationStatus === 'PendingConsensus') {
          <p role="status">
            Hay una discrepancia de puntuación en esta muestra.
            <a [routerLink]="['/judge', 'tables', tableId, 'discrepancies']">
              Resolver discrepancia
            </a>
          </p>
        } @else if (currentSample.evaluationStatus !== 'NotStarted') {
          <p role="status">Esta muestra ya ha sido evaluada.</p>
        } @else {
          <form [formGroup]="form" (ngSubmit)="onSubmit()" class="evaluation-sheet-form">
            <nav class="section-nav" aria-label="Secciones de la hoja de cata">
              @for (section of sections; track section.key) {
                <button
                  type="button"
                  class="section-nav__item"
                  [class.is-active]="activeTab() === section.key"
                  [attr.aria-current]="activeTab() === section.key ? 'step' : null"
                  (click)="goToTab(section.key)"
                >
                  {{ section.label }}
                  @if (sectionValid(section)) {
                    <span class="section-nav__done" aria-hidden="true">✓</span>
                  }
                </button>
              }
              <button
                type="button"
                class="section-nav__item section-nav__item--summary"
                [class.is-active]="activeTab() === 'summary'"
                [attr.aria-current]="activeTab() === 'summary' ? 'step' : null"
                (click)="goToTab('summary')"
              >
                Resumen
              </button>
            </nav>

            <div class="evaluation-card">
              @if (activeSection(); as section) {
                <fieldset class="evaluation-section">
                  <legend>{{ section.label }} (0–{{ section.max }})</legend>

                  <div class="descriptor-group">
                    @switch (section.key) {
                      @case ('appearance') {
                        <div class="descriptor-field">
                          <div class="descriptor-field__header">
                            <label for="appearance-color">Color</label>
                            <label class="descriptor-field__inappropriate">
                              <input
                                type="checkbox"
                                [checked]="descriptors().appearance.colorInappropriate"
                                aria-label="Color: inapropiado para el estilo"
                                (change)="
                                  setAppearance('colorInappropriate', checkboxValue($event))
                                "
                              />
                              Inapropiado
                            </label>
                          </div>
                          <select
                            id="appearance-color"
                            [value]="descriptors().appearance.color"
                            (change)="setAppearance('color', selectValue($event))"
                          >
                            <option value="">Sin especificar</option>
                            @for (opt of colorOptions; track opt.value) {
                              <option [value]="opt.value">{{ opt.label }}</option>
                            }
                          </select>
                          @if (descriptors().appearance.color === 'Other') {
                            <bp-input
                              label="Especifica el color"
                              id="appearance-color-other"
                              [value]="descriptors().appearance.colorOther"
                              (valueChange)="setAppearance('colorOther', $event)"
                            ></bp-input>
                          }
                        </div>

                        <div class="descriptor-field">
                          <div class="descriptor-field__header">
                            <label for="appearance-clarity">Claridad</label>
                            <label class="descriptor-field__inappropriate">
                              <input
                                type="checkbox"
                                [checked]="descriptors().appearance.clarityInappropriate"
                                aria-label="Claridad: inapropiado para el estilo"
                                (change)="
                                  setAppearance('clarityInappropriate', checkboxValue($event))
                                "
                              />
                              Inapropiado
                            </label>
                          </div>
                          <select
                            id="appearance-clarity"
                            [value]="descriptors().appearance.clarity"
                            (change)="setAppearance('clarity', selectValue($event))"
                          >
                            <option value="">Sin especificar</option>
                            @for (opt of clarityOptions; track opt.value) {
                              <option [value]="opt.value">{{ opt.label }}</option>
                            }
                          </select>
                        </div>

                        <div class="descriptor-field">
                          <div class="descriptor-field__header">
                            <label for="appearance-foam">Espuma</label>
                            <label class="descriptor-field__inappropriate">
                              <input
                                type="checkbox"
                                [checked]="descriptors().appearance.foamInappropriate"
                                aria-label="Espuma: inapropiado para el estilo"
                                (change)="setAppearance('foamInappropriate', checkboxValue($event))"
                              />
                              Inapropiado
                            </label>
                          </div>
                          <select
                            id="appearance-foam"
                            [value]="descriptors().appearance.foam"
                            (change)="setAppearance('foam', selectValue($event))"
                          >
                            <option value="">Sin especificar</option>
                            @for (opt of foamOptions; track opt.value) {
                              <option [value]="opt.value">{{ opt.label }}</option>
                            }
                          </select>
                          @if (descriptors().appearance.foam === 'Other') {
                            <bp-input
                              label="Especifica la espuma"
                              id="appearance-foam-other"
                              [value]="descriptors().appearance.foamOther"
                              (valueChange)="setAppearance('foamOther', $event)"
                            ></bp-input>
                          }
                        </div>

                        <bp-bipolar-slider
                          id="appearance-retention"
                          label="Retención"
                          startLabel="Baja"
                          endLabel="Alta"
                          [value]="descriptors().appearance.retention ?? 50"
                          [inappropriate]="descriptors().appearance.retentionInappropriate"
                          (valueChange)="setAppearance('retention', $event)"
                          (inappropriateChange)="setAppearance('retentionInappropriate', $event)"
                        ></bp-bipolar-slider>

                        <bp-input
                          label="Textura"
                          id="appearance-texture"
                          [value]="descriptors().appearance.texture"
                          (valueChange)="setAppearance('texture', $event)"
                        ></bp-input>

                        <bp-textarea
                          label="Otros"
                          id="appearance-notes"
                          [rows]="2"
                          [value]="descriptors().appearance.notes"
                          (valueChange)="setAppearance('notes', $event)"
                        ></bp-textarea>
                      }
                      @case ('aroma') {
                        <bp-discrete-slider
                          id="aroma-malt"
                          label="Malta"
                          [value]="descriptors().aroma.malt ?? 0"
                          [inappropriate]="descriptors().aroma.maltInappropriate"
                          (valueChange)="setAroma('malt', $event)"
                          (inappropriateChange)="setAroma('maltInappropriate', $event)"
                        ></bp-discrete-slider>

                        <bp-discrete-slider
                          id="aroma-hops"
                          label="Lúpulos"
                          [value]="descriptors().aroma.hops ?? 0"
                          [inappropriate]="descriptors().aroma.hopsInappropriate"
                          (valueChange)="setAroma('hops', $event)"
                          (inappropriateChange)="setAroma('hopsInappropriate', $event)"
                        ></bp-discrete-slider>

                        <bp-discrete-slider
                          id="aroma-fermentation"
                          label="Fermentación"
                          [value]="descriptors().aroma.fermentation ?? 0"
                          [inappropriate]="descriptors().aroma.fermentationInappropriate"
                          (valueChange)="setAroma('fermentation', $event)"
                          (inappropriateChange)="setAroma('fermentationInappropriate', $event)"
                        ></bp-discrete-slider>
                      }
                      @case ('flavor') {
                        <bp-discrete-slider
                          id="flavor-malt"
                          label="Malta"
                          [value]="descriptors().flavor.malt ?? 0"
                          [inappropriate]="descriptors().flavor.maltInappropriate"
                          (valueChange)="setFlavor('malt', $event)"
                          (inappropriateChange)="setFlavor('maltInappropriate', $event)"
                        ></bp-discrete-slider>
                        <bp-discrete-slider
                          id="flavor-hops"
                          label="Lúpulos"
                          [value]="descriptors().flavor.hops ?? 0"
                          [inappropriate]="descriptors().flavor.hopsInappropriate"
                          (valueChange)="setFlavor('hops', $event)"
                          (inappropriateChange)="setFlavor('hopsInappropriate', $event)"
                        ></bp-discrete-slider>
                        <bp-discrete-slider
                          id="flavor-bitterness"
                          label="Amargor"
                          [value]="descriptors().flavor.bitterness ?? 0"
                          [inappropriate]="descriptors().flavor.bitternessInappropriate"
                          (valueChange)="setFlavor('bitterness', $event)"
                          (inappropriateChange)="setFlavor('bitternessInappropriate', $event)"
                        ></bp-discrete-slider>
                        <bp-discrete-slider
                          id="flavor-fermentation"
                          label="Fermentación"
                          [value]="descriptors().flavor.fermentation ?? 0"
                          [inappropriate]="descriptors().flavor.fermentationInappropriate"
                          (valueChange)="setFlavor('fermentation', $event)"
                          (inappropriateChange)="setFlavor('fermentationInappropriate', $event)"
                        ></bp-discrete-slider>
                        <bp-bipolar-slider
                          id="flavor-balance"
                          label="Equilibrio"
                          startLabel="Lupulado"
                          endLabel="Maltoso"
                          [value]="descriptors().flavor.balance ?? 50"
                          [inappropriate]="descriptors().flavor.balanceInappropriate"
                          (valueChange)="setFlavor('balance', $event)"
                          (inappropriateChange)="setFlavor('balanceInappropriate', $event)"
                        ></bp-bipolar-slider>
                        <bp-bipolar-slider
                          id="flavor-finish"
                          label="Final / Retrogusto"
                          startLabel="Seco"
                          endLabel="Dulce"
                          [value]="descriptors().flavor.finish ?? 50"
                          [inappropriate]="descriptors().flavor.finishInappropriate"
                          (valueChange)="setFlavor('finish', $event)"
                          (inappropriateChange)="setFlavor('finishInappropriate', $event)"
                        ></bp-bipolar-slider>
                      }
                      @case ('mouthfeel') {
                        <bp-discrete-slider
                          id="mouthfeel-body"
                          label="Cuerpo"
                          [value]="descriptors().mouthfeel.body ?? 0"
                          [inappropriate]="descriptors().mouthfeel.bodyInappropriate"
                          (valueChange)="setMouthfeel('body', $event)"
                          (inappropriateChange)="setMouthfeel('bodyInappropriate', $event)"
                        ></bp-discrete-slider>

                        <bp-discrete-slider
                          id="mouthfeel-carbonation"
                          label="Carbonatación"
                          [value]="descriptors().mouthfeel.carbonation ?? 0"
                          [inappropriate]="descriptors().mouthfeel.carbonationInappropriate"
                          (valueChange)="setMouthfeel('carbonation', $event)"
                          (inappropriateChange)="setMouthfeel('carbonationInappropriate', $event)"
                        ></bp-discrete-slider>
                        <bp-discrete-slider
                          id="mouthfeel-alcohol-warmth"
                          label="Calor alcohólico"
                          [value]="descriptors().mouthfeel.alcoholWarmth ?? 0"
                          [inappropriate]="descriptors().mouthfeel.alcoholWarmthInappropriate"
                          (valueChange)="setMouthfeel('alcoholWarmth', $event)"
                          (inappropriateChange)="setMouthfeel('alcoholWarmthInappropriate', $event)"
                        ></bp-discrete-slider>

                        <bp-discrete-slider
                          id="mouthfeel-creaminess"
                          label="Cremosidad"
                          [value]="descriptors().mouthfeel.creaminess ?? 0"
                          [inappropriate]="descriptors().mouthfeel.creaminessInappropriate"
                          (valueChange)="setMouthfeel('creaminess', $event)"
                          (inappropriateChange)="setMouthfeel('creaminessInappropriate', $event)"
                        ></bp-discrete-slider>

                        <bp-discrete-slider
                          id="mouthfeel-astringency"
                          label="Astringencia"
                          [value]="descriptors().mouthfeel.astringency ?? 0"
                          [inappropriate]="descriptors().mouthfeel.astringencyInappropriate"
                          (valueChange)="setMouthfeel('astringency', $event)"
                          (inappropriateChange)="setMouthfeel('astringencyInappropriate', $event)"
                        ></bp-discrete-slider>

                        <bp-textarea
                          label="Otros"
                          id="mouthfeel-notes"
                          [rows]="2"
                          [value]="descriptors().mouthfeel.notes"
                          (valueChange)="setMouthfeel('notes', $event)"
                        ></bp-textarea>
                      }
                      @case ('overall') {
                        <bp-bipolar-slider
                          id="overall-classic-example"
                          label="Fidelidad al estilo"
                          startLabel="Ejemplo clásico"
                          endLabel="No acorde al estilo"
                          [value]="descriptors().overall.classicExample ?? 50"
                          [inappropriate]="descriptors().overall.classicExampleInappropriate"
                          (valueChange)="setOverall('classicExample', $event)"
                          (inappropriateChange)="setOverall('classicExampleInappropriate', $event)"
                        ></bp-bipolar-slider>
                        <bp-bipolar-slider
                          id="overall-defects"
                          label="Defectos"
                          startLabel="Sin defectos"
                          endLabel="Defectos significativos"
                          [value]="descriptors().overall.defects ?? 50"
                          [inappropriate]="descriptors().overall.defectsInappropriate"
                          (valueChange)="setOverall('defects', $event)"
                          (inappropriateChange)="setOverall('defectsInappropriate', $event)"
                        ></bp-bipolar-slider>
                        <bp-bipolar-slider
                          id="overall-vitality"
                          label="Vitalidad"
                          startLabel="Maravillosa"
                          endLabel="Sin vida"
                          [value]="descriptors().overall.vitality ?? 50"
                          [inappropriate]="descriptors().overall.vitalityInappropriate"
                          (valueChange)="setOverall('vitality', $event)"
                          (inappropriateChange)="setOverall('vitalityInappropriate', $event)"
                        ></bp-bipolar-slider>
                      }
                    }
                  </div>

                  <div class="score-group">
                    <bp-input
                      type="number"
                      label="Puntuación"
                      [id]="section.key + '-score'"
                      [min]="0"
                      [max]="section.max"
                      [required]="true"
                      [formControl]="control(section.scoreControl)"
                      [hasError]="hasScoreError(section)"
                      [errorMessage]="'Indica una puntuación entre 0 y ' + section.max + '.'"
                    ></bp-input>

                    <bp-textarea
                      label="Comentario"
                      [id]="section.key + '-comment'"
                      [rows]="4"
                      [required]="true"
                      [formControl]="control(section.commentControl)"
                      [hasError]="hasCommentError(section)"
                      [errorMessage]="commentErrorMessage(section)"
                      [hint]="commentHint(section)"
                    ></bp-textarea>
                  </div>
                </fieldset>
              } @else {
                <section class="review-summary" aria-label="Revisión antes de enviar">
                  <h2>Revisa tu evaluación</h2>

                  @for (section of sections; track section.key) {
                    <div class="review-row">
                      <span class="review-row__label">{{ section.label }}</span>
                      <span class="review-row__score">
                        {{ scoreValue(section) }} / {{ section.max }}
                      </span>
                      <p class="review-row__comment">{{ commentValue(section) }}</p>
                    </div>
                  }

                  <p class="total-display">
                    Total (de solo lectura, calculado por el servidor al enviar): {{ total() }}
                  </p>

                  <fieldset class="off-flavors">
                    <legend>Descriptores de defecto detectados</legend>
                    <div class="off-flavor-grid">
                      @for (opt of offFlavorOptions; track opt.value) {
                        <label class="off-flavor-option">
                          <input
                            type="checkbox"
                            [checked]="hasOffFlavor(opt.value)"
                            (change)="toggleOffFlavor(opt.value)"
                          />
                          {{ opt.label }}
                        </label>
                      }
                    </div>
                  </fieldset>

                  <bp-textarea
                    label="Feedback general"
                    id="feedback"
                    hint="Comentarios sobre el estilo, receta, procesos y sensaciones al beber. Incluye sugerencias de mejora."
                    [rows]="4"
                    formControlName="feedback"
                  ></bp-textarea>
                </section>

                @if (submitError(); as message) {
                  <p role="alert">{{ message }}</p>
                }

                <bp-step-actions
                  [showBack]="false"
                  nextType="submit"
                  nextLabel="Enviar evaluación"
                  [nextDisabled]="form.invalid || submitting()"
                  [nextLoading]="submitting()"
                ></bp-step-actions>
              }
            </div>
          </form>
        }
      }
    </bp-page-shell>
  `,
  styles: `
    .offline-badge {
      background: #fef3c7;
      color: #92400e;
      padding: 0.5rem 0.75rem;
      border-radius: 0.5rem;
      font-weight: 600;
      display: inline-block;
    }

    .sample-style {
      color: #4b5563;
    }

    /* Organizer follow-up (Session 2026-09-21): the sheet used to inherit bp-page-shell's full
       88rem page-container width unconditionally, which read as too wide/empty on a desktop
       screen. Capping it here (not in the shared shell, which other screens still rely on at full
       width) keeps this one judge-facing form centered and book-like instead of stretched. Same
       cap on .evaluation-sheet-header (blind code, style line, style reference panel) so that
       header stays aligned with the form beneath it instead of sitting at the shell's full width. */
    .evaluation-sheet-header,
    .evaluation-sheet-form {
      max-width: 56rem;
      margin: 0 auto;
      width: 100%;
    }

    .evaluation-sheet-header {
      display: flex;
      flex-direction: column;
      gap: var(--spacing-1);
    }

    .evaluation-sheet-header > h1,
    .evaluation-sheet-header > .sample-style {
      margin: 0;
    }

    .evaluation-sheet-form {
      display: flex;
      flex-direction: column;
      gap: var(--spacing-3);
    }

    .section-nav {
      display: flex;
      gap: var(--spacing-2);
      overflow-x: auto;
      padding: var(--spacing-2) 0;
      -webkit-overflow-scrolling: touch;
    }

    .section-nav__item {
      flex: none;
      display: inline-flex;
      align-items: center;
      gap: var(--spacing-1);
      min-height: 40px;
      padding: 0 var(--spacing-4);
      border-radius: var(--radius-full);
      border: 1.5px solid var(--color-bp-border-strong);
      background: var(--color-bp-surface);
      color: var(--color-bp-text-muted);
      font-weight: 600;
      font-size: 0.875rem;
      cursor: pointer;
      white-space: nowrap;
    }

    .section-nav__item.is-active {
      background: var(--color-bp-cobre-600);
      border-color: var(--color-bp-cobre-600);
      color: #fff;
    }

    .section-nav__item:focus-visible {
      outline: 2px solid var(--color-bp-cobre-500);
      outline-offset: 2px;
    }

    .section-nav__done {
      color: var(--color-bp-exito-600);
    }

    .section-nav__item.is-active .section-nav__done {
      color: #fff;
    }

    .evaluation-card {
      border: 1px solid var(--color-bp-border);
      border-radius: var(--radius-lg);
      background: var(--color-bp-surface);
      padding: var(--spacing-6);
      --bp-step-actions-inset: var(--spacing-6);
    }

    .evaluation-section {
      border: 0;
      padding: 0;
      margin: 0;
    }

    .evaluation-section legend {
      font-family: 'Fraunces', serif;
      font-size: 1.25rem;
      font-weight: 600;
      color: var(--color-bp-text);
      padding: 0 0 var(--spacing-4);
    }

    .descriptor-group {
      display: flex;
      flex-direction: column;
    }

    /* Desktop only: two fields side by side instead of one long single-column list — the sheet
       is capped well under the viewport width above (.evaluation-sheet-form), so this is what
       actually puts the freed-up horizontal space to use rather than leaving it as dead margin.
       Free-text fields (Textura, Otros/Notas) stay full-width — a two-column textarea reads
       worse, not better. */
    @media (min-width: 720px) {
      .descriptor-group {
        display: grid;
        grid-template-columns: repeat(2, minmax(0, 1fr));
        column-gap: var(--spacing-6);
        align-items: start;
      }

      .descriptor-group > bp-input,
      .descriptor-group > bp-textarea {
        grid-column: 1 / -1;
      }
    }

    .descriptor-field {
      margin: var(--spacing-3) 0;
      display: flex;
      flex-direction: column;
      gap: var(--spacing-2);
    }

    .descriptor-field__header {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: var(--spacing-2) var(--spacing-3);
    }

    .descriptor-field__header > label[for] {
      font-size: 0.875rem;
      font-weight: 600;
      color: var(--color-bp-text);
    }

    .descriptor-field__inappropriate {
      flex: none;
      display: flex;
      align-items: center;
      gap: var(--spacing-1);
      font-size: 0.75rem;
      font-weight: 500;
      color: var(--color-bp-text-muted);
      white-space: nowrap;
    }

    .descriptor-field select {
      min-height: 44px;
      padding: 0 var(--spacing-4);
      font: inherit;
      font-size: 1rem;
      color: var(--color-bp-text);
      background: var(--color-bp-surface);
      border: 1.5px solid var(--color-bp-border-strong);
      border-radius: var(--radius-md);
    }

    /* Score/comment now come after the descriptors (organizer follow-up, Session 2026-09-21) — a
       visible divider marks where the "official" scored part of the section starts. */
    .score-group {
      margin-top: var(--spacing-6);
      padding-top: var(--spacing-4);
      border-top: 1px solid var(--color-bp-border);
      display: flex;
      flex-direction: column;
    }

    .review-summary {
      display: flex;
      flex-direction: column;
      gap: var(--spacing-4);
    }

    .review-summary h2 {
      font-family: 'Fraunces', serif;
      font-size: 1.25rem;
      font-weight: 600;
      margin: 0;
      color: var(--color-bp-text);
    }

    .review-row {
      border: 1px solid var(--color-bp-border);
      border-radius: var(--radius-md);
      padding: var(--spacing-3) var(--spacing-4);
    }

    .review-row__label {
      font-weight: 600;
    }

    .review-row__score {
      float: right;
      font-weight: 600;
      color: var(--color-bp-cobre-700);
    }

    .review-row__comment {
      margin: var(--spacing-2) 0 0;
      color: var(--color-bp-text-muted);
      clear: both;
    }

    .total-display {
      font-weight: 600;
      margin: 0;
    }

    .off-flavors {
      border: 1px solid var(--color-bp-border);
      border-radius: var(--radius-md);
      padding: var(--spacing-4);
      margin: 0;
    }

    .off-flavors legend {
      font-weight: 600;
      padding: 0 var(--spacing-2);
    }

    .off-flavor-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(10rem, 1fr));
      gap: var(--spacing-2) var(--spacing-4);
    }

    .off-flavor-option {
      display: flex;
      align-items: center;
      gap: var(--spacing-2);
      font-size: 0.875rem;
    }

    @media (max-width: 640px) {
      .evaluation-card {
        padding: var(--spacing-4);
        --bp-step-actions-inset: var(--spacing-4);
      }
    }
  `,
})
export class EvaluationSheetComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(TastingOrderApiService);
  private readonly syncService = inject(SyncService);
  private readonly hub = inject(CompetitionHubService);

  protected readonly tableId = this.route.snapshot.paramMap.get('tableId')!;
  protected readonly beerEntryId = this.route.snapshot.paramMap.get('beerEntryId')!;

  protected readonly sections = SECTIONS;
  protected readonly minCommentLength = MIN_COMMENT_LENGTH;
  protected readonly form = buildForm();

  protected readonly colorOptions = COLOR_OPTIONS;
  protected readonly clarityOptions = CLARITY_OPTIONS;
  protected readonly foamOptions = FOAM_OPTIONS;
  protected readonly offFlavorOptions = OFF_FLAVOR_OPTIONS;

  protected readonly sample = signal<JudgeSample | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly submitting = signal(false);
  protected readonly submitError = signal<string | null>(null);
  protected readonly isOffline = signal(!navigator.onLine);

  // Free navigation (Session 2026-09-21): a judge can jump to any section, or the summary, at any
  // time — clicking a nav-bar item never gates on the currently-active section's own validity.
  protected readonly activeTab = signal<ActiveTab>(SECTIONS[0].key);

  protected readonly activeSection = computed<EvaluationSectionConfig | null>(() => {
    const tab = this.activeTab();
    return tab === 'summary' ? null : SECTIONS.find((section) => section.key === tab)!;
  });

  protected readonly descriptors = signal<DescriptorsFormState>(initialDescriptorsState());

  // Best-effort, consulted only for the ejection notice's table name (see handleEjected below) —
  // nothing else in this component displays or depends on the table's name. Mirrors
  // judge-table-order.component.ts's own tableName fallback exactly, for ejection-notice parity
  // between the two judge-facing screens a JudgeRemoved event can strike.
  protected readonly tableSummary = signal<JudgeTableSummary | null>(null);
  protected readonly tableName = computed(() => this.tableSummary()?.name ?? 'Mesa');

  private readonly onlineListener = () => this.isOffline.set(false);
  private readonly offlineListener = () => this.isOffline.set(true);
  private judgeRemovedSubscription: Subscription | null = null;

  constructor() {
    void this.initialize();
  }

  ngOnInit(): void {
    window.addEventListener('online', this.onlineListener);
    window.addEventListener('offline', this.offlineListener);
    void this.connectToHub();
  }

  ngOnDestroy(): void {
    window.removeEventListener('online', this.onlineListener);
    window.removeEventListener('offline', this.offlineListener);
    this.judgeRemovedSubscription?.unsubscribe();
    void this.hub.leaveTable(this.tableId).catch(() => {
      // Best-effort: leaving on navigation-away is a courtesy, not a functional requirement.
    });
  }

  protected goToTab(tab: ActiveTab): void {
    this.activeTab.set(tab);
  }

  // The score/comment inputs live in one @if block that is reused across section tabs, so they
  // bind via [formControl] rather than formControlName: FormControlName only resolves its control
  // once, on first change, and would keep every tab writing into the first section's controls.
  // FormControlDirective re-binds whenever the control instance it is given changes.
  protected control(name: string): FormControl {
    return this.form.get(name) as FormControl;
  }

  protected sectionValid(section: EvaluationSectionConfig): boolean {
    return (
      !!this.form.get(section.scoreControl)?.valid && !!this.form.get(section.commentControl)?.valid
    );
  }

  protected hasScoreError(section: EvaluationSectionConfig): boolean {
    const control = this.form.get(section.scoreControl);
    return !!control && control.invalid && (control.touched || control.dirty);
  }

  protected hasCommentError(section: EvaluationSectionConfig): boolean {
    const control = this.form.get(section.commentControl);
    return !!control && control.invalid && (control.touched || control.dirty);
  }

  protected commentErrorMessage(section: EvaluationSectionConfig): string {
    const remaining = this.remainingChars(section.key);
    if (remaining === 0) {
      return '';
    }
    return remaining === 1
      ? `Falta 1 carácter (mínimo ${MIN_COMMENT_LENGTH}).`
      : `Faltan ${remaining} caracteres (mínimo ${MIN_COMMENT_LENGTH}).`;
  }

  protected commentHint(section: EvaluationSectionConfig): string {
    return this.form.get(section.commentControl)?.valid ? 'Longitud mínima alcanzada.' : '';
  }

  protected scoreValue(section: EvaluationSectionConfig): number | null {
    return this.form.get(section.scoreControl)?.value as number | null;
  }

  protected commentValue(section: EvaluationSectionConfig): string {
    return (this.form.get(section.commentControl)?.value as string) ?? '';
  }

  protected remainingChars(key: SectionKey): number {
    const section = SECTIONS.find((s) => s.key === key)!;
    const value = (this.form.get(section.commentControl)?.value as string) ?? '';
    return Math.max(0, MIN_COMMENT_LENGTH - value.length);
  }

  protected total(): number {
    return SECTIONS.reduce((sum, section) => {
      const value = this.form.get(section.scoreControl)?.value as number | null;
      return sum + (value ?? 0);
    }, 0);
  }

  // Plain (change)-event helpers for the native <select>/<input type="checkbox"> descriptor
  // controls, matching this codebase's established manual-binding convention for non-validated
  // fields (categories-step.component.ts's own <select> handlers) rather than fighting Angular's
  // SelectControlValueAccessor over a null-vs-empty-string default.
  protected selectValue(event: Event): string {
    return (event.target as HTMLSelectElement).value;
  }

  protected checkboxValue(event: Event): boolean {
    return (event.target as HTMLInputElement).checked;
  }

  protected setAppearance<K extends keyof AppearanceDescriptorsState>(
    field: K,
    value: AppearanceDescriptorsState[K],
  ): void {
    this.descriptors.update((current) => ({
      ...current,
      appearance: { ...current.appearance, [field]: value },
    }));
    this.saveDraftNow();
  }

  protected setAroma<K extends keyof AromaDescriptorsState>(
    field: K,
    value: AromaDescriptorsState[K],
  ): void {
    this.descriptors.update((current) => ({
      ...current,
      aroma: { ...current.aroma, [field]: value },
    }));
    this.saveDraftNow();
  }

  protected setFlavor<K extends keyof FlavorDescriptorsState>(
    field: K,
    value: FlavorDescriptorsState[K],
  ): void {
    this.descriptors.update((current) => ({
      ...current,
      flavor: { ...current.flavor, [field]: value },
    }));
    this.saveDraftNow();
  }

  protected setMouthfeel<K extends keyof MouthfeelDescriptorsState>(
    field: K,
    value: MouthfeelDescriptorsState[K],
  ): void {
    this.descriptors.update((current) => ({
      ...current,
      mouthfeel: { ...current.mouthfeel, [field]: value },
    }));
    this.saveDraftNow();
  }

  protected setOverall<K extends keyof OverallDescriptorsState>(
    field: K,
    value: OverallDescriptorsState[K],
  ): void {
    this.descriptors.update((current) => ({
      ...current,
      overall: { ...current.overall, [field]: value },
    }));
    this.saveDraftNow();
  }

  protected hasOffFlavor(term: string): boolean {
    return this.descriptors().offFlavors.has(term);
  }

  protected toggleOffFlavor(term: string): void {
    this.descriptors.update((current) => {
      const offFlavors = new Set(current.offFlavors);
      if (offFlavors.has(term)) {
        offFlavors.delete(term);
      } else {
        offFlavors.add(term);
      }
      return { ...current, offFlavors };
    });
    this.saveDraftNow();
  }

  protected async onSubmit(): Promise<void> {
    if (this.form.invalid || this.submitting()) {
      return;
    }
    this.submitting.set(true);
    this.submitError.set(null);

    const { scores, comments, descriptors, feedback } = this.buildPayload();
    // The idempotency key's documented format is {competitionId}:{tableId}:{judgeId}:{entryId},
    // but no judge-facing endpoint currently exposes competitionId/judgeId to the frontend, and
    // the backend only checks the header's *presence*, never its content (the real idempotency
    // guarantee is the server-side (judge, entry) unique constraint) — a judge only ever evaluates
    // a given entry once at a given table, so (tableId, beerEntryId) is already sufficient here.
    const idempotencyKey = `${this.tableId}:${this.beerEntryId}`;

    try {
      await this.syncService.submit(
        idempotencyKey,
        this.tableId,
        this.beerEntryId,
        scores,
        comments,
        descriptors,
        feedback,
      );
      // Both a confirmed and a merely-enqueued outcome navigate back: the offline-first guarantee
      // is that the judge's submit action is instant and durable regardless of connectivity — the
      // sample's status will reflect the real state on the next fetch/reconcile of the table, and
      // SyncService itself clears the draft once a later replay actually confirms it server-side.
      await this.router.navigate(['/judge', 'tables', this.tableId]);
    } catch (error) {
      this.submitting.set(false);

      // T087/US12: submitting after this judge was removed mid-session hits the same
      // JudgeTableAccess membership guard as every other judge-workspace endpoint, which 404s (no
      // dedicated urn) the instant RemovedAt is set server-side. Eject immediately rather than
      // just showing an inert error message and relying solely on the live JudgeRemoved hub event
      // (handleJudgeRemovedEvent below) to eventually catch it — that event may never arrive (a
      // dropped/reconnecting connection), whereas a 404'd submit is itself definitive proof of
      // removal, right now.
      if (error instanceof ApiError && error.status === 404) {
        await this.handleEjected();
        return;
      }

      this.submitError.set(this.describeSubmitError(error));
    }
  }

  private buildPayload(): {
    scores: EvaluationScores;
    comments: EvaluationComments;
    descriptors: EvaluationDescriptors;
    feedback: string;
  } {
    const raw = this.form.getRawValue() as Record<string, number | string>;
    return {
      scores: {
        aroma: raw['aromaScore'] as number,
        appearance: raw['appearanceScore'] as number,
        flavor: raw['flavorScore'] as number,
        mouthfeel: raw['mouthfeelScore'] as number,
        overall: raw['overallScore'] as number,
      },
      comments: {
        aroma: raw['aromaComment'] as string,
        appearance: raw['appearanceComment'] as string,
        flavor: raw['flavorComment'] as string,
        mouthfeel: raw['mouthfeelComment'] as string,
        overall: raw['overallComment'] as string,
      },
      descriptors: toDescriptorsPayload(this.descriptors()),
      feedback: raw['feedback'] as string,
    };
  }

  private describeSubmitError(error: unknown): string {
    if (!(error instanceof ApiError)) {
      // SyncService's submit() only rejects with a non-ApiError when the durable outbox write
      // itself failed (storage unavailable) — the spec edge case that the judge must be warned
      // immediately rather than silently losing offline protection.
      return 'No hemos podido guardar esta evaluación localmente (el almacenamiento de tu dispositivo puede estar lleno o restringido). Vuelve a intentarlo, o usa otro dispositivo o navegador.';
    }

    switch (error.urn) {
      case 'urn:birrapoint:order-not-fixed':
        return 'El orden de cata de esta mesa todavía no ha sido fijado.';
      case 'urn:birrapoint:out-of-sequence':
        return 'Esta muestra no es la siguiente en tu orden de cata. Vuelve atrás y actualiza la mesa.';
      case 'urn:birrapoint:table-closed':
        return 'Esta mesa ha sido cerrada; ya no se pueden enviar más evaluaciones.';
      case 'urn:birrapoint:invalid-state-transition':
        return 'Esta competición no está actualmente abierta para evaluación.';
      default:
        return errorMessage(error);
    }
  }

  private async initialize(): Promise<void> {
    this.loadSample();
    this.loadTableSummary();

    const draft = await this.syncService.loadDraft(this.beerEntryId);
    if (draft) {
      this.form.patchValue(
        {
          aromaScore: draft.scores.aroma,
          aromaComment: draft.comments.aroma,
          appearanceScore: draft.scores.appearance,
          appearanceComment: draft.comments.appearance,
          flavorScore: draft.scores.flavor,
          flavorComment: draft.comments.flavor,
          mouthfeelScore: draft.scores.mouthfeel,
          mouthfeelComment: draft.comments.mouthfeel,
          overallScore: draft.scores.overall,
          overallComment: draft.comments.overall,
          feedback: draft.feedback ?? '',
        },
        { emitEvent: false },
      );
      this.descriptors.set(fromDescriptorsPayload(draft.descriptors));
    }

    // Every field change is durably drafted (FR-026/SC-003) — debounced inside SyncService itself,
    // this subscription just forwards the current values plainly on each change. A rejection
    // (storage unavailable) is swallowed here rather than surfaced per-keystroke; the same failure
    // mode is caught loudly at submit time instead, when it actually blocks the judge's progress.
    this.form.valueChanges.subscribe(() => this.saveDraftNow());
  }

  // Shared by the FormGroup's own valueChanges subscription (initialize() above) and every
  // descriptor setter (setAppearance/setAroma/.../toggleOffFlavor) — a descriptor-only edit must
  // debounce-persist exactly like a score/comment keystroke does (FR-026/SC-003), not just the
  // five validated fields.
  private saveDraftNow(): void {
    const { scores, comments, descriptors, feedback } = this.buildPayload();
    this.syncService
      .saveDraft(this.beerEntryId, this.tableId, scores, comments, descriptors, feedback)
      .catch(() => {
        // Best-effort per keystroke; submit() surfaces a storage failure loudly instead.
      });
  }

  private loadSample(): void {
    this.loadError.set(null);
    this.api.getTableSamples(this.tableId).subscribe({
      next: (samples) => {
        const match = samples.find((s) => s.beerEntryId === this.beerEntryId) ?? null;
        if (!match) {
          this.loadError.set('No se ha encontrado esta muestra en esta mesa.');
          return;
        }
        this.sample.set(match);
        cacheSample(match);
      },
      error: (error: unknown) => {
        const apiError = toGenericApiError(error);
        // status 0 = the request never reached the server at all (offline/connectivity failure,
        // not a real domain rejection returned by the API) -- if this exact sample was already
        // fetched successfully earlier in the session, fall back to that snapshot rather than
        // stranding an in-progress (or freshly-restarted, still-offline) sheet behind a load error
        // it can never resolve without connectivity (US7 AC3/quickstart scenario 7: "restart the
        // app [while offline], verify data intact"). Any other failure (a real 404/403 the server
        // actively returned) still blocks the view exactly as before -- there is no legitimate
        // sample to fall back to in that case.
        if (apiError.status === 0) {
          const cached = readCachedSample(this.beerEntryId);
          if (cached) {
            this.sample.set(cached);
            return;
          }
        }
        this.loadError.set(errorMessage(apiError));
      },
    });
  }

  // Best-effort: this component otherwise never needs the table's display name — only the
  // ejection notice on /judge/tables does (see handleEjected below), so a failure here just
  // leaves that notice with the generic fallback rather than blocking anything on this screen.
  private loadTableSummary(): void {
    this.api.getMyTables().subscribe({
      next: (tables) => {
        const summary = tables.find((table) => table.tableId === this.tableId) ?? null;
        this.tableSummary.set(summary);
      },
      error: () => {
        // Best-effort: see comment above.
      },
    });
  }

  private async connectToHub(): Promise<void> {
    try {
      await this.hub.start();
      await this.hub.joinTable(this.tableId);
      this.judgeRemovedSubscription = this.hub
        .on('JudgeRemoved')
        .pipe(filter((event: JudgeRemovedEvent) => event.tableId === this.tableId))
        .subscribe(() => this.handleJudgeRemovedEvent());
    } catch {
      // Realtime is a best-effort notification channel (contracts/signalr-hub.md): a judge who
      // never gets this live notification while mid-sheet is still caught the moment they try to
      // submit (onSubmit's own 404 handling above ejects directly, without waiting on this
      // channel at all) or, failing that, by the offline engine's lazy-discovery 404 purge
      // (SyncService.attemptOne) on a later background replay.
    }
  }

  // T087/US12 — same reasoning as judge-table-order.component.ts's handler: this DTO never carries
  // this session's own judgeId, so a JudgeRemoved event for this table can't be matched directly.
  // Re-verify membership instead; a 404 means it was this judge, anything else means it wasn't.
  private handleJudgeRemovedEvent(): void {
    this.api.getTableSamples(this.tableId).subscribe({
      next: () => {
        // Still a member — the removed judge was someone else at this table. No-op.
      },
      error: (error: unknown) => {
        if (toGenericApiError(error).status === 404) {
          void this.handleEjected();
        }
      },
    });
  }

  private async handleEjected(): Promise<void> {
    void this.syncService.rejectOutboxForTable(this.tableId).catch(() => {
      // Best-effort: a failure here only leaves the stale row for the next lazy-discovery 404
      // purge in SyncService's background replay to clean up instead.
    });
    await this.router.navigate(['/judge', 'tables'], {
      state: { ejected: true, tableName: this.tableName() },
    });
  }
}
