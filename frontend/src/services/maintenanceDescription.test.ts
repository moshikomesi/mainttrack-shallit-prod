import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import {
  buildMaintenanceLogV1Description,
  buildMaintenanceLogV2Description,
  selectNarrativeTranslation,
  viewMaintenanceDescription,
} from './maintenanceDescription.ts';
import type { MaintenanceTranslationsResponse } from '../types/maintenance.ts';

const narrative = 'Seal leaking. Replaced the gasket.';

describe('maintenance description sentinels', () => {
  it('parses a marked V1 report as narrative only, including a component-like first line', () => {
    const stored = buildMaintenanceLogV1Description('Hydraulic pump\nSeal leaking.');
    const view = viewMaintenanceDescription(stored);

    assert.equal(stored.split('\n')[0], 'maintenanceLog.v1');
    assert.equal(view?.version, 'v1');
    assert.equal(view?.component, null);
    assert.equal(view?.narrative, 'Hydraulic pump\nSeal leaking.');
    assert.equal(view?.narrative.includes('maintenanceLog.v1'), false);
  });

  it('keeps catalog, Other, and custom component lines out of the translated narrative', () => {
    const cases = [
      {
        component: { kind: 'mapped' as const, code: 'bearing', nameKey: 'maintenanceComponent.bearing' },
        componentLine: 'maintenanceComponent.bearing',
      },
      {
        component: { kind: 'other' as const, text: '   ' },
        componentLine: 'maintenanceComponent.other',
      },
      {
        component: { kind: 'other' as const, text: 'Hydraulic pump' },
        componentLine: 'Hydraulic pump',
      },
      {
        component: { kind: 'other' as const, text: 'Other: Hydraulic pump' },
        componentLine: 'Other: Hydraulic pump',
      },
    ];

    for (const item of cases) {
      const stored = buildMaintenanceLogV2Description(item.component, narrative);
      const view = viewMaintenanceDescription(stored);
      const lines = stored.split('\n');

      assert.equal(lines[0], 'maintenanceLog.v2');
      assert.equal(lines[1], item.componentLine);
      assert.equal(view?.version, 'v2');
      assert.equal(view?.component, item.componentLine);
      assert.equal(view?.narrative, narrative);
      assert.equal(view?.narrative.includes(item.componentLine), false);
      assert.equal(view?.narrative.includes('maintenanceLog.v2'), false);
    }
  });

  it('does not treat a component-only V2 report as translatable narrative', () => {
    const stored = buildMaintenanceLogV2Description(
      { kind: 'other', text: 'Hydraulic pump' },
      '   '
    );
    const view = viewMaintenanceDescription(stored);

    assert.equal(view?.version, 'v2');
    assert.equal(view?.component, 'Hydraulic pump');
    assert.equal(view?.narrative, '');
  });

  it('keeps unmarked descriptions on the historical first-line split', () => {
    const v1Shaped = viewMaintenanceDescription(narrative);
    assert.equal(v1Shaped?.version, 'legacy');
    assert.equal(v1Shaped?.component, narrative);
    assert.equal(v1Shaped?.narrative, '');

    const v2Shaped = viewMaintenanceDescription(`Hydraulic pump\n${narrative}`);
    assert.equal(v2Shaped?.version, 'legacy');
    assert.equal(v2Shaped?.component, 'Hydraulic pump');
    assert.equal(v2Shaped?.narrative, narrative);

    const catalog = viewMaintenanceDescription('maintenanceComponent.bearing\nSeal leaking.');
    assert.equal(catalog?.version, 'legacy');
    assert.equal(catalog?.component, 'maintenanceComponent.bearing');
    assert.equal(catalog?.narrative, 'Seal leaking.');
  });

  it('composes Clearance display lines without sentinels', () => {
    /** Same original-language composition ClearanceScreen uses (before catalog resolve). */
    const clearanceLines = (description: string): string[] => {
      const view = viewMaintenanceDescription(description);
      if (!view) return [];
      const lines: string[] = [];
      if (view.component) lines.push(view.component);
      if (view.narrative) lines.push(view.narrative);
      return lines;
    };

    const v1 = buildMaintenanceLogV1Description(narrative);
    assert.deepEqual(clearanceLines(v1), [narrative]);
    assert.equal(clearanceLines(v1).join('\n').includes('maintenanceLog'), false);

    const v2 = buildMaintenanceLogV2Description(
      { kind: 'mapped', code: 'bearing', nameKey: 'maintenanceComponent.bearing' },
      narrative
    );
    assert.deepEqual(clearanceLines(v2), ['maintenanceComponent.bearing', narrative]);
    assert.equal(clearanceLines(v2).join('\n').includes('maintenanceLog.v2'), false);

    const legacy = `Hydraulic pump\n${narrative}`;
    assert.deepEqual(clearanceLines(legacy), ['Hydraulic pump', narrative]);
  });
});

describe('narrative translation selection', () => {
  const response: MaintenanceTranslationsResponse = {
    originalText: narrative,
    translations: [
      { language: 'he', status: 'pending', text: null },
      { language: 'en', status: 'completed', text: 'Seal leak fixed.' },
      { language: 'th', status: 'unavailable', text: null },
    ],
  };

  it('returns the completed translation for the selected UI language', () => {
    assert.equal(selectNarrativeTranslation(response, narrative, 'en'), 'Seal leak fixed.');
  });

  it('falls back to the original narrative while pending, unavailable, or missing', () => {
    assert.equal(selectNarrativeTranslation(response, narrative, 'he'), null);
    assert.equal(selectNarrativeTranslation(response, narrative, 'th'), null);
    assert.equal(selectNarrativeTranslation({ originalText: narrative, translations: [] }, narrative, 'en'), null);
    assert.equal(selectNarrativeTranslation(null, narrative, 'en'), null);
  });

  it('ignores a response that belongs to a different narrative', () => {
    const otherReport: MaintenanceTranslationsResponse = {
      originalText: 'Belt slipping on the roller.',
      translations: [
        { language: 'he', status: 'completed', text: 'רצועה מחליקה.' },
        { language: 'en', status: 'completed', text: 'Belt slips.' },
        { language: 'th', status: 'completed', text: 'สายพานลื่น' },
      ],
    };

    for (const language of ['he', 'en', 'th']) {
      assert.equal(selectNarrativeTranslation(otherReport, narrative, language), null);
    }
    assert.equal(selectNarrativeTranslation(otherReport, 'Belt slipping on the roller.', 'th'), 'สายพานลื่น');
    assert.equal(selectNarrativeTranslation(response, '', 'en'), null);
  });

  it('does not repeat a translation that is empty or identical to the original', () => {
    const same: MaintenanceTranslationsResponse = {
      originalText: narrative,
      translations: [
        { language: 'en', status: 'completed', text: ` ${narrative} ` },
        { language: 'he', status: 'completed', text: '   ' },
      ],
    };
    assert.equal(selectNarrativeTranslation(same, narrative, 'en'), null);
    assert.equal(selectNarrativeTranslation(same, narrative, 'he'), null);
  });
});
