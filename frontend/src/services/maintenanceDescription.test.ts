import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, it } from 'node:test';
import {
  buildMaintenanceDescriptionForSubmit,
  buildMaintenanceLogV1Description,
  buildMaintenanceLogV2Description,
  formatMaintenanceDescriptionDisplay,
  selectNarrativeTranslation,
  viewMaintenanceDescription,
} from './maintenanceDescription.ts';
import { translations, type Language } from '../i18n/translations.ts';
import type { MaintenanceTranslationsResponse } from '../types/maintenance.ts';

function translate(language: Language): (key: string) => string {
  return (key: string) => translations[key]?.[language] || key;
}

function visibleText(componentLabel: string | null, narrative: string): string {
  return [componentLabel, narrative].filter((part) => part && part.length > 0).join('\n');
}

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

  it('treats a legacy description without a component key as one narrative', () => {
    const narrativeOnly = viewMaintenanceDescription(`  ${narrative}  `);
    assert.equal(narrativeOnly?.version, 'legacy');
    assert.equal(narrativeOnly?.component, null);
    assert.equal(narrativeOnly?.narrative, narrative);

    const freeText = viewMaintenanceDescription(`Hydraulic pump\n${narrative}`);
    assert.equal(freeText?.version, 'legacy');
    assert.equal(freeText?.component, null);
    assert.equal(freeText?.narrative, `Hydraulic pump\n${narrative}`);
  });

  it('splits a legacy component key from the narrative and ignores a component-only description', () => {
    const catalog = viewMaintenanceDescription('maintenanceComponent.bearing\r\nSeal leaking.');
    assert.equal(catalog?.version, 'legacy');
    assert.equal(catalog?.component, 'maintenanceComponent.bearing');
    assert.equal(catalog?.narrative, 'Seal leaking.');
    assert.equal(catalog?.narrative.includes('maintenanceComponent.'), false);

    const componentOnly = viewMaintenanceDescription('  maintenanceComponent.driveShaft  \n   ');
    assert.equal(componentOnly?.version, 'legacy');
    assert.equal(componentOnly?.component, 'maintenanceComponent.driveShaft');
    assert.equal(componentOnly?.narrative, '');
  });

  it('composes Clearance display lines without sentinels', () => {
    const clearanceLines = (description: string, maintenanceTypeCode: string | null): string[] => {
      const display = formatMaintenanceDescriptionDisplay(description, maintenanceTypeCode, translate('en'));
      return [display.componentLabel, display.narrative].filter((line): line is string => Boolean(line));
    };

    const v1 = buildMaintenanceLogV1Description(narrative);
    assert.deepEqual(clearanceLines(v1, 'other'), [narrative]);
    assert.equal(clearanceLines(v1, 'bearing').join('\n').includes('maintenanceLog'), false);

    const v2 = buildMaintenanceLogV2Description(
      { kind: 'mapped', code: 'bearing', nameKey: 'maintenanceComponent.bearing' },
      narrative
    );
    assert.deepEqual(clearanceLines(v2, 'other'), ['Bearing', narrative]);
    assert.equal(clearanceLines(v2, 'bearing').join('\n').includes('maintenanceLog.v2'), false);
    assert.equal(clearanceLines(v2, 'bearing').join('\n').includes('maintenanceComponent.'), false);

    const legacy = `Hydraulic pump\n${narrative}`;
    assert.deepEqual(clearanceLines(legacy, 'other'), [legacy]);
    assert.deepEqual(clearanceLines(legacy, 'bearing'), [legacy]);

    const catalog = `maintenanceComponent.driveShaft\n${narrative}`;
    assert.deepEqual(clearanceLines(catalog, 'bearing'), ['Drive shaft', narrative]);
    assert.equal(clearanceLines(catalog, 'bearing').join('\n').includes('maintenanceComponent.'), false);
  });
});

describe('maintenance description display', () => {
  const languages: Language[] = ['he', 'en', 'th'];
  const driveShaft = 'maintenanceLog.v2\nmaintenanceComponent.driveShaft\nSeal leaking. Replaced the gasket.';

  it('renders a V2 component key in Hebrew, English, and Thai', () => {
    for (const language of languages) {
      const display = formatMaintenanceDescriptionDisplay(driveShaft, 'motor', translate(language));
      const expected = translations['maintenanceComponent.driveShaft'][language];

      assert.equal(display.version, 'v2');
      assert.equal(display.componentLabel, expected);
      assert.equal(display.narrative, narrative);
      const shown = visibleText(display.componentLabel, display.narrative);
      assert.equal(shown.includes('maintenanceLog.v2'), false);
      assert.equal(shown.includes('maintenanceComponent.'), false);
      assert.equal(shown.includes(narrative), true);
    }
  });

  it('keeps a whitespace-padded sentinel as legacy narrative text, matching the backend', () => {
    const body = 'maintenanceComponent.driveShaft\nSeal leaking. Replaced the gasket.';
    const cases = [
      { stored: ` maintenanceLog.v2 \n${body}`, expected: `maintenanceLog.v2 \n${body}` },
      { stored: `maintenanceLog.v2 \n${body}`, expected: `maintenanceLog.v2 \n${body}` },
      { stored: ` maintenanceLog.v1\n${narrative}`, expected: `maintenanceLog.v1\n${narrative}` },
    ];

    for (const { stored, expected } of cases) {
      const view = viewMaintenanceDescription(stored);
      assert.equal(view?.version, 'legacy');
      assert.equal(view?.component, null);
      assert.equal(view?.narrative, expected);

      const display = formatMaintenanceDescriptionDisplay(stored, null, translate('he'));
      assert.equal(display.version, 'legacy');
      assert.equal(display.componentLabel, null);
      assert.equal(display.narrative, expected);
    }

    const exact = viewMaintenanceDescription(`maintenanceLog.v2\n${body}`);
    assert.equal(exact?.version, 'v2');
    assert.equal(exact?.component, 'maintenanceComponent.driveShaft');
  });

  it('trims the first line only to detect a legacy component key', () => {
    const view = viewMaintenanceDescription(`  maintenanceComponent.driveShaft  \n${narrative}`);
    assert.equal(view?.version, 'legacy');
    assert.equal(view?.component, 'maintenanceComponent.driveShaft');
    assert.equal(view?.narrative, narrative);
  });

  it('does not expose a missing component key', () => {
    const stored = 'maintenanceLog.v2\nmaintenanceComponent.notInCatalog\nSeal leaking. Replaced the gasket.';
    for (const language of languages) {
      const display = formatMaintenanceDescriptionDisplay(stored, 'other', translate(language));
      const shown = visibleText(display.componentLabel, display.narrative);
      assert.equal(display.componentLabel, translations['common.notProvided'][language]);
      assert.equal(shown.includes('maintenanceComponent.notInCatalog'), false);
      assert.equal(shown.includes('maintenanceLog.v2'), false);
      assert.equal(display.narrative, narrative);
    }
  });

  it('keeps a V1 narrative and a legacy free-text description intact', () => {
    const v1 = formatMaintenanceDescriptionDisplay(
      buildMaintenanceLogV1Description(`Hydraulic pump\n${narrative}`),
      'bearing',
      translate('en')
    );
    assert.equal(v1.version, 'v1');
    assert.equal(v1.componentLabel, null);
    assert.equal(v1.narrative, `Hydraulic pump\n${narrative}`);
    assert.equal(visibleText(v1.componentLabel, v1.narrative).includes('maintenanceLog.v1'), false);

    const legacy = `Hydraulic pump\n${narrative}`;
    const plain = formatMaintenanceDescriptionDisplay(legacy, 'bearing', translate('th'));
    assert.equal(plain.version, 'legacy');
    assert.equal(plain.componentLabel, null);
    assert.equal(plain.narrative, legacy);
  });

  it('localizes a legacy component key in Hebrew, English, and Thai without repeating it in the narrative', () => {
    const stored = `maintenanceComponent.driveShaft\n${narrative}`;
    const translation: MaintenanceTranslationsResponse = {
      originalText: narrative,
      translations: [
        { language: 'he', status: 'completed', text: 'האטם דולף. האטם הוחלף.' },
        { language: 'en', status: 'completed', text: 'Seal leak fixed.' },
        { language: 'th', status: 'completed', text: 'ซีลรั่ว เปลี่ยนปะเก็นแล้ว' },
      ],
    };

    for (const language of languages) {
      const display = formatMaintenanceDescriptionDisplay(stored, 'bearing', translate(language));
      const label = translations['maintenanceComponent.driveShaft'][language];
      const translated = selectNarrativeTranslation(translation, display.narrative, language);
      const shown = visibleText(display.componentLabel, translated ?? display.narrative);

      assert.equal(display.version, 'legacy');
      assert.equal(display.componentLabel, label);
      assert.equal(display.narrative, narrative);
      assert.equal(shown.includes('maintenanceComponent.'), false);
      assert.equal(shown.includes(label), true);
      assert.equal(translated?.includes(label), false);
      assert.equal(translated?.includes('maintenanceComponent.'), false);
    }

    const componentOnly = formatMaintenanceDescriptionDisplay(
      'maintenanceComponent.driveShaft',
      null,
      translate('he')
    );
    assert.equal(componentOnly.componentLabel, 'ציר מניע');
    assert.equal(componentOnly.narrative, '');
    assert.equal(selectNarrativeTranslation(translation, componentOnly.narrative, 'he'), null);
  });

  it('keeps a custom V2 component name and does not translate it as a key', () => {
    const stored = buildMaintenanceLogV2Description({ kind: 'other', text: 'Hydraulic pump' }, narrative);
    const display = formatMaintenanceDescriptionDisplay(stored, 'other', translate('he'));
    assert.equal(display.componentLabel, 'Hydraulic pump');
    assert.equal(display.narrative, narrative);
    assert.equal(visibleText(display.componentLabel, display.narrative).includes('maintenanceLog.v2'), false);
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

describe('submitted "other" description is marked exactly once', () => {
  const driveShaft = {
    kind: 'mapped' as const,
    code: 'driveShaft',
    nameKey: 'maintenanceComponent.driveShaft',
  };

  it('submits a V2 description unchanged', () => {
    const v2 = buildMaintenanceLogV2Description(driveShaft, narrative);
    const submitted = buildMaintenanceDescriptionForSubmit(v2);

    assert.equal(submitted, v2);
    assert.equal(submitted.split('\n')[0], 'maintenanceLog.v2');
    assert.equal(submitted.includes('maintenanceLog.v1'), false);
  });

  it('still wraps a plain-text other description as V1', () => {
    const submitted = buildMaintenanceDescriptionForSubmit('Hydraulic pump\nSeal leaking.');

    assert.equal(submitted, 'maintenanceLog.v1\nHydraulic pump\nSeal leaking.');
    assert.equal(submitted, buildMaintenanceLogV1Description('Hydraulic pump\nSeal leaking.'));
    assert.equal(buildMaintenanceDescriptionForSubmit('  Belt slipping.  '), 'maintenanceLog.v1\nBelt slipping.');
    assert.equal(buildMaintenanceDescriptionForSubmit('   '), '');
  });

  it('does not wrap an existing V1 description a second time', () => {
    const v1 = buildMaintenanceLogV1Description(narrative);

    assert.equal(buildMaintenanceDescriptionForSubmit(v1), v1);
    assert.equal(buildMaintenanceDescriptionForSubmit(v1.replace(/\n/g, '\r\n')), v1.replace(/\n/g, '\r\n'));
    assert.equal(buildMaintenanceDescriptionForSubmit(v1).split('maintenanceLog.v1').length - 1, 1);
  });

  it('keeps a component-only V2 description valid', () => {
    const submitted = buildMaintenanceDescriptionForSubmit(buildMaintenanceLogV2Description(driveShaft, ''));
    const view = viewMaintenanceDescription(submitted);
    const display = formatMaintenanceDescriptionDisplay(submitted, 'other', translate('he'));

    assert.equal(submitted, 'maintenanceLog.v2\nmaintenanceComponent.driveShaft');
    assert.equal(view?.version, 'v2');
    assert.equal(view?.component, 'maintenanceComponent.driveShaft');
    assert.equal(view?.narrative, '');
    assert.equal(display.componentLabel, translations['maintenanceComponent.driveShaft'].he);
    assert.equal(display.narrative, '');
    assert.equal(visibleText(display.componentLabel, display.narrative).includes('maintenance'), false);
  });

  it('keeps a V2 component-plus-narrative description valid', () => {
    const submitted = buildMaintenanceDescriptionForSubmit(buildMaintenanceLogV2Description(driveShaft, narrative));
    const view = viewMaintenanceDescription(submitted);
    const display = formatMaintenanceDescriptionDisplay(submitted, 'other', translate('he'));

    assert.equal(submitted, `maintenanceLog.v2\nmaintenanceComponent.driveShaft\n${narrative}`);
    assert.equal(view?.version, 'v2');
    assert.equal(view?.component, 'maintenanceComponent.driveShaft');
    assert.equal(view?.narrative, narrative);
    assert.equal(display.componentLabel, translations['maintenanceComponent.driveShaft'].he);
    assert.equal(display.narrative, narrative);
  });

  it('keeps a custom V2 component name valid', () => {
    const submitted = buildMaintenanceDescriptionForSubmit(
      buildMaintenanceLogV2Description({ kind: 'other', text: 'Hydraulic pump' }, narrative)
    );
    assert.equal(submitted, `maintenanceLog.v2\nHydraulic pump\n${narrative}`);
    assert.equal(viewMaintenanceDescription(submitted)?.component, 'Hydraulic pump');
  });

  it('the batch submit builds an "other" payload through this helper only', () => {
    const source = readFileSync(new URL('./maintenanceBatchSubmit.ts', import.meta.url), 'utf8');

    assert.match(source, /description:\s*buildMaintenanceDescriptionForSubmit\(row\.fault\)/);
    assert.equal(/buildMaintenanceLogV1Description/.test(source), false);
    assert.match(source, /if \(isOther\) \{/);
    assert.match(source, /return \{ \.\.\.base \};/);
  });
});
