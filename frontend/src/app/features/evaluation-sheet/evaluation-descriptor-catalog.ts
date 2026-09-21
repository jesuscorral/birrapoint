/**
 * Session 2026-09-21 (FR-063): the closed-list options for the structured tasting descriptors,
 * mirroring backend's EvaluationDescriptorCatalog (Features/Evaluations/EvaluationDescriptors.cs)
 * token-for-token — the English `value` is what's actually sent on the wire (must match the
 * backend's validator or the submit gets rejected with a 400); `label` is the Spanish text shown
 * to the judge. Kept here rather than deriving Spanish from English at runtime, matching this
 * app's established convention (backend enums stay English, e.g. EvaluationStatus, the frontend
 * maps them to Spanish for display — see judge-table-order.component.ts's own badge switch).
 */
export interface DescriptorOption {
  value: string;
  label: string;
}

export const COLOR_OPTIONS: DescriptorOption[] = [
  { value: 'Yellow', label: 'Amarillo' },
  { value: 'Golden', label: 'Dorado' },
  { value: 'Amber', label: 'Ámbar' },
  { value: 'Copper', label: 'Cobrizo' },
  { value: 'Brown', label: 'Marrón' },
  { value: 'Black', label: 'Negro' },
  { value: 'Other', label: 'Otros' },
];

export const CLARITY_OPTIONS: DescriptorOption[] = [
  { value: 'Clear', label: 'Cristalina' },
  { value: 'Hazy', label: 'Turbia' },
  { value: 'Opaque', label: 'Opaca' },
];

export const FOAM_OPTIONS: DescriptorOption[] = [
  { value: 'White', label: 'Blanca' },
  { value: 'Ivory', label: 'Marfil' },
  { value: 'Beige', label: 'Beige' },
  { value: 'Tan', label: 'Canela' },
  { value: 'Brown', label: 'Marrón' },
  { value: 'Other', label: 'Otros' },
];

/** The paper sheet's 20-term off-flavor descriptor sidebar checklist. */
export const OFF_FLAVOR_OPTIONS: DescriptorOption[] = [
  { value: 'Acetaldehyde', label: 'Acetaldehído' },
  { value: 'SourAcidic', label: 'Agrio/Ácido' },
  { value: 'Smoky', label: 'Ahumado' },
  { value: 'Alcoholic', label: 'Alcohólico' },
  { value: 'Astringent', label: 'Astringente' },
  { value: 'Brettanomyces', label: 'Brettanomyces' },
  { value: 'Diacetyl', label: 'Diacetil' },
  { value: 'Dms', label: 'DMS' },
  { value: 'Spicy', label: 'Especiado' },
  { value: 'Estery', label: 'Esteroso' },
  { value: 'Lightstruck', label: 'Golpe de luz' },
  { value: 'Herbal', label: 'Herbal' },
  { value: 'Medicinal', label: 'Medicinal' },
  { value: 'Metallic', label: 'Metálico' },
  { value: 'Oxidized', label: 'Oxidación' },
  { value: 'Plastic', label: 'Plástico' },
  { value: 'MustyMoldy', label: 'Rancio/Mohoso' },
  { value: 'SolventFusel', label: 'Solvente/Fusel' },
  { value: 'Sulfury', label: 'Sulfuroso' },
  { value: 'Vegetal', label: 'Vegetal' },
];

/** Labels for the 4-stop discrete intensity sliders (Malta, Lúpulos, Fermentación, Cuerpo, ...). */
export const INTENSITY_LABELS: readonly string[] = ['Nada', 'Bajo', 'Medio', 'Alto'];
