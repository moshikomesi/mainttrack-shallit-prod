import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, it } from 'node:test';
import { localizeMaintenanceDescription } from './maintenanceDescription.ts';
import {
  createMaintenanceTranslationCache,
  hasPendingTranslations,
} from './maintenanceTranslationCache.ts';
import { translations, type Language } from '../i18n/translations.ts';
import type {
  MaintenanceTranslationItemDto,
  MaintenanceTranslationsResponse,
} from '../types/maintenance.ts';

const languages: Language[] = ['he', 'en', 'th'];
const narrative = 'Seal leaking. Replaced the gasket.';
const stored = `maintenanceComponent.driveShaft\n${narrative}`;
const storedV2 = `maintenanceLog.v2\nmaintenanceComponent.driveShaft\n${narrative}`;

const translated: Record<Language, string> = {
  he: 'האטם דולף. האטם הוחלף.',
  en: 'The seal leaked. The gasket was replaced.',
  th: 'ซีลรั่ว เปลี่ยนปะเก็นแล้ว',
};

function translate(language: Language): (key: string) => string {
  return (key: string) => translations[key]?.[language] || key;
}

function response(items: MaintenanceTranslationItemDto[], originalText = narrative): MaintenanceTranslationsResponse {
  return { originalText, translations: items };
}

const allCompleted = response(
  languages.map((language) => ({ language, status: 'completed' as const, text: translated[language] }))
);

function shown(display: { componentLabel: string | null; narrative: string }): string {
  return [display.componentLabel, display.narrative].filter(Boolean).join('\n');
}

describe('completed translation replaces the original narrative', () => {
  for (const language of languages) {
    it(`uses the ${language} translation instead of the source narrative`, () => {
      for (const description of [stored, storedV2]) {
        const display = localizeMaintenanceDescription(description, 'other', allCompleted, language, translate(language));

        assert.equal(display.translated, true);
        assert.equal(display.narrative, translated[language]);
        assert.equal(display.componentLabel, translations['maintenanceComponent.driveShaft'][language]);
        assert.equal(shown(display).includes(narrative), false);
        assert.equal(shown(display).split(translated[language]).length - 1, 1);
      }
    });
  }

  it('selects the translation for the new UI language without another request', () => {
    const texts = languages.map((language) =>
      localizeMaintenanceDescription(stored, 'other', allCompleted, language, translate(language))
    );

    assert.deepEqual(
      texts.map((display) => display.narrative),
      [translated.he, translated.en, translated.th]
    );
    assert.deepEqual(
      texts.map((display) => display.componentLabel),
      languages.map((language) => translations['maintenanceComponent.driveShaft'][language])
    );
  });

  it('shows a translated V1 narrative and a narrative-only legacy description', () => {
    const v1 = localizeMaintenanceDescription(`maintenanceLog.v1\n${narrative}`, 'bearing', allCompleted, 'en', translate('en'));
    assert.equal(v1.narrative, translated.en);
    assert.equal(v1.componentLabel, null);

    const legacy = localizeMaintenanceDescription(narrative, null, allCompleted, 'th', translate('th'));
    assert.equal(legacy.narrative, translated.th);
    assert.equal(legacy.componentLabel, null);
  });
});

describe('fallback to the original narrative', () => {
  const cases: Array<[string, MaintenanceTranslationsResponse | null]> = [
    ['no response', null],
    ['pending', response([{ language: 'en', status: 'pending', text: null }])],
    ['unavailable', response([{ language: 'en', status: 'unavailable', text: null }])],
    ['missing language', response([{ language: 'he', status: 'completed', text: translated.he }])],
    ['empty text', response([{ language: 'en', status: 'completed', text: '   ' }])],
    ['null text', response([{ language: 'en', status: 'completed', text: null }])],
    ['identical to source', response([{ language: 'en', status: 'completed', text: ` ${narrative} ` }])],
    ['another narrative', response([{ language: 'en', status: 'completed', text: 'Other' }], 'Different narrative')],
  ];

  for (const [name, value] of cases) {
    it(`keeps the original for ${name}`, () => {
      const display = localizeMaintenanceDescription(stored, 'other', value, 'en', translate('en'));

      assert.equal(display.translated, false);
      assert.equal(display.narrative, narrative);
      assert.equal(display.componentLabel, 'Drive shaft');
    });
  }
});

describe('internal markers are never shown', () => {
  it('localizes component keys and hides sentinels in every language and fallback state', () => {
    const states = [allCompleted, null, response([{ language: 'en', status: 'pending', text: null }])];
    for (const description of [stored, storedV2, `maintenanceLog.v1\n${narrative}`]) {
      for (const state of states) {
        for (const language of languages) {
          const display = localizeMaintenanceDescription(description, 'other', state, language, translate(language));
          const text = shown(display);
          assert.equal(text.includes('maintenanceComponent.'), false);
          assert.equal(text.includes('maintenanceLog.'), false);
        }
      }
    }
  });

  it('replaces an unknown component key with the shared not-provided label', () => {
    const display = localizeMaintenanceDescription(
      `maintenanceComponent.notInCatalog\n${narrative}`,
      null,
      allCompleted,
      'he',
      translate('he')
    );
    assert.equal(display.componentLabel, translations['common.notProvided'].he);
    assert.equal(shown(display).includes('maintenanceComponent.'), false);
  });

  it('does not translate a component-only description', () => {
    const display = localizeMaintenanceDescription('maintenanceComponent.driveShaft', null, allCompleted, 'en', translate('en'));
    assert.equal(display.componentLabel, 'Drive shaft');
    assert.equal(display.narrative, '');
    assert.equal(display.translated, false);
  });
});

describe('maintenance translation cache', () => {
  const ok = (id: string): MaintenanceTranslationsResponse => response([], `text ${id}`);

  async function settle(): Promise<void> {
    await new Promise((resolve) => setTimeout(resolve, 0));
  }

  it('requests each report once across repeated renders, language changes, and list growth', async () => {
    const calls: string[] = [];
    const cache = createMaintenanceTranslationCache(async (id) => {
      calls.push(id);
      return ok(id);
    });

    cache.request(['a', 'b']);
    cache.request(['a', 'b']);
    cache.request(['a', 'b', 'c']);
    await settle();
    cache.request(['a', 'b', 'c', 'd']);
    cache.request(['a', 'b', 'c', 'd']);
    await settle();

    assert.deepEqual([...calls].sort(), ['a', 'b', 'c', 'd']);
    assert.equal(cache.get('c')?.originalText, 'text c');
    assert.equal(cache.get('missing'), null);
  });

  it('keeps one failed report from affecting the others and does not retry it', async () => {
    const calls: string[] = [];
    const errors: string[] = [];
    const cache = createMaintenanceTranslationCache(
      async (id) => {
        calls.push(id);
        if (id === 'bad') throw new Error('boom');
        return ok(id);
      },
      { onError: (id) => errors.push(id) }
    );

    cache.request(['a', 'bad', 'c']);
    await settle();
    cache.request(['a', 'bad', 'c']);
    await settle();

    assert.equal(cache.get('a')?.originalText, 'text a');
    assert.equal(cache.get('bad'), null);
    assert.equal(cache.get('c')?.originalText, 'text c');
    assert.deepEqual(errors, ['bad']);
    assert.equal(calls.filter((id) => id === 'bad').length, 1);
  });

  it('contains a synchronous fetcher failure', async () => {
    const cache = createMaintenanceTranslationCache(() => {
      throw new Error('sync');
    });
    cache.request(['a']);
    await settle();
    assert.equal(cache.get('a'), null);
  });

  it('limits simultaneous requests', async () => {
    let active = 0;
    let peak = 0;
    const cache = createMaintenanceTranslationCache(
      async (id) => {
        active += 1;
        peak = Math.max(peak, active);
        await new Promise((resolve) => setTimeout(resolve, 2));
        active -= 1;
        return ok(id);
      },
      { concurrency: 2 }
    );

    cache.request(['1', '2', '3', '4', '5']);
    await new Promise((resolve) => setTimeout(resolve, 30));

    assert.equal(peak, 2);
    assert.equal(cache.get('5')?.originalText, 'text 5');
  });

  it('notifies subscribers when an entry settles and stops after unsubscribe', async () => {
    const cache = createMaintenanceTranslationCache(async (id) => ok(id));
    let notified = 0;
    const unsubscribe = cache.subscribe(() => {
      notified += 1;
    });

    cache.request(['a']);
    await settle();
    assert.equal(notified, 1);

    unsubscribe();
    cache.request(['b']);
    await settle();
    assert.equal(notified, 1);
  });

  it('refreshes pending entries up to the cap until settled, then stops', async () => {
    let calls = 0;
    const cache = createMaintenanceTranslationCache(
      async () => {
        calls += 1;
        if (calls < 3) {
          return response([
            { language: 'he', status: 'unavailable', text: null },
            { language: 'en', status: 'pending', text: null },
            { language: 'th', status: 'pending', text: null },
          ]);
        }
        return response([
          { language: 'he', status: 'unavailable', text: null },
          { language: 'en', status: 'completed', text: translated.en },
          { language: 'th', status: 'completed', text: translated.th },
        ]);
      },
      { pendingRefreshIntervalMs: 5, maxPendingRefreshes: 5 }
    );

    cache.request(['p1']);
    await new Promise((resolve) => setTimeout(resolve, 45));

    assert.equal(calls, 3);
    const ready = cache.get('p1');
    assert.equal(hasPendingTranslations(ready), false);
    assert.equal(ready?.translations.find((item) => item.language === 'en')?.text, translated.en);
    cache.dispose();
  });

  it('does not refresh entries without pending translations (source omitted as unavailable)', async () => {
    let calls = 0;
    const cache = createMaintenanceTranslationCache(
      async () => {
        calls += 1;
        return response([
          { language: 'he', status: 'unavailable', text: null },
          { language: 'en', status: 'completed', text: translated.en },
          { language: 'th', status: 'completed', text: translated.th },
        ]);
      },
      { pendingRefreshIntervalMs: 5, maxPendingRefreshes: 5 }
    );

    cache.request(['done']);
    await new Promise((resolve) => setTimeout(resolve, 35));

    assert.equal(calls, 1);
    cache.dispose();
  });

  it('caps bounded refreshes for persistently pending entries and preserves last response on refresh failure', async () => {
    let calls = 0;
    const errors: string[] = [];
    const cache = createMaintenanceTranslationCache(
      async () => {
        calls += 1;
        if (calls === 2) throw new Error('transient refresh error');
        return response([
          { language: 'he', status: 'completed', text: translated.he },
          { language: 'en', status: 'pending', text: null },
        ]);
      },
      {
        pendingRefreshIntervalMs: 5,
        maxPendingRefreshes: 3,
        onError: (id) => errors.push(id),
      }
    );

    cache.request(['p2']);
    await new Promise((resolve) => setTimeout(resolve, 40));

    assert.equal(calls, 2);
    assert.deepEqual(errors, ['p2']);
    assert.equal(cache.get('p2')?.translations.find((item) => item.language === 'he')?.text, translated.he);
    cache.dispose();
  });

  it('dispose cancels scheduled pending refreshes', async () => {
    let calls = 0;
    const cache = createMaintenanceTranslationCache(
      async () => {
        calls += 1;
        return response([{ language: 'en', status: 'pending', text: null }]);
      },
      { pendingRefreshIntervalMs: 15, maxPendingRefreshes: 3 }
    );

    cache.request(['p3']);
    await settle();
    assert.equal(calls, 1);

    cache.dispose();
    await new Promise((resolve) => setTimeout(resolve, 35));
    assert.equal(calls, 1);
  });

  it('stops refreshing once maxPendingRefreshes is reached', async () => {
    let calls = 0;
    const cache = createMaintenanceTranslationCache(
      async () => {
        calls += 1;
        return response([{ language: 'en', status: 'pending', text: null }]);
      },
      { pendingRefreshIntervalMs: 5, maxPendingRefreshes: 2 }
    );

    cache.request(['p4']);
    await new Promise((resolve) => setTimeout(resolve, 45));

    // 1 initial request + 2 bounded refreshes = 3 total.
    assert.equal(calls, 3);
    cache.dispose();
  });

  it('dispose while queued allows remount subscribe and request to fetch dropped entries', async () => {
    let releaseFirst!: () => void;
    const firstGate = new Promise<void>((resolve) => {
      releaseFirst = resolve;
    });
    const calls: string[] = [];
    const cache = createMaintenanceTranslationCache(
      async (id) => {
        calls.push(id);
        if (id === '1') await firstGate;
        return ok(id);
      },
      { concurrency: 1 }
    );

    cache.request(['1', '2']);
    cache.dispose();
    releaseFirst();
    await settle();

    const unsub = cache.subscribe(() => {});
    cache.request(['1', '2']);
    await settle();
    unsub();
    cache.dispose();

    assert.deepEqual(calls, ['1', '2']);
    assert.equal(cache.get('2')?.originalText, 'text 2');
  });
});

describe('screens render one description', () => {
  const read = (path: string) => readFileSync(new URL(path, import.meta.url), 'utf8');
  const details = read('../components/ReportDetailsScreen.tsx');
  const list = read('../components/ReportsListScreen.tsx');
  const clearance = read('../components/ClearanceScreen.tsx');
  const translationsSource = read('../i18n/translations.ts');

  it('details screen has no separate Translation row', () => {
    assert.equal(details.includes('faultTranslation'), false);
    assert.equal(details.includes('narrativeTranslation'), false);
    assert.equal(translationsSource.includes('details.faultTranslation'), false);
    assert.equal((details.match(/t\('log\.fault'\)/g) ?? []).length, 2);
    assert.equal((details.match(/maintenanceDescription\.narrative\}/g) ?? []).length, 2);
  });

  it('every maintenance description rendering uses the localized display', () => {
    assert.equal(details.includes('localizeMaintenanceDescription('), true);
    assert.equal(list.includes('localizeMaintenanceDescription('), true);
    assert.equal(clearance.includes('localizeMaintenanceDescription('), true);

    assert.equal(/>\s*\{\s*maintenanceReport\.description\s*\}/.test(details), false);
    assert.equal(/>\s*\{\s*report\.description\s*\}/.test(list), false);
    assert.equal(/>\s*\{\s*item\.description\s*\}/.test(clearance), false);
  });

  it('list and Clearance use the language-aware cached translation lookup', () => {
    for (const source of [list, clearance]) {
      assert.equal(source.includes('useMaintenanceTranslations('), true);
      assert.equal(source.includes('getMaintenanceTranslations('), false);
    }
    assert.equal(list.includes('getMaintenanceTranslation(report.id)'), true);
    assert.equal(clearance.includes('getMaintenanceTranslation(item.maintenanceEntryId)'), true);
    assert.equal(list.includes('language,'), true);
    assert.equal(clearance.includes('language={language}'), true);
  });

  it('Clearance requests translations only for maintenance entries', () => {
    const start = clearance.indexOf('const translatableEntryIds');
    const end = clearance.indexOf('useMaintenanceTranslations(translatableEntryIds)');
    assert.ok(start > 0 && end > start);
    const block = clearance.slice(start, end);
    assert.equal(block.includes("item.sourceType === 'maintenance_entry'"), true);
    assert.equal(block.includes('item.maintenanceEntryId'), true);
    assert.equal(block.includes('morning_round_v2'), false);
  });
});

describe('Hebrew presentation is consistent across report surfaces', () => {
  const read = (path: string) => readFileSync(new URL(path, import.meta.url), 'utf8');
  const surfaces = {
    details: read('../components/ReportDetailsScreen.tsx'),
    list: read('../components/ReportsListScreen.tsx'),
    clearance: read('../components/ClearanceScreen.tsx'),
  };
  const hebrewSource = 'האטם דולף. הוחלף האטם.';
  const hebrewOriginalResponse = response(
    [
      { language: 'he', status: 'completed', text: 'האטם דולף. האטם הוחלף.' },
      { language: 'en', status: 'completed', text: 'The seal leaked.' },
      { language: 'th', status: 'completed', text: 'ซีลรั่ว' },
    ],
    hebrewSource
  );
  const stores = [
    { name: 'V2', description: `maintenanceLog.v2\nmaintenanceComponent.driveShaft\n${hebrewSource}`, component: true },
    { name: 'legacy component', description: `maintenanceComponent.driveShaft\n${hebrewSource}`, component: true },
    { name: 'legacy narrative only', description: hebrewSource, component: false },
  ];

  for (const store of stores) {
    it(`shows one Hebrew translation with its own component label (${store.name})`, () => {
      const display = localizeMaintenanceDescription(store.description, 'other', hebrewOriginalResponse, 'he', translate('he'));
      const text = shown(display);

      assert.equal(display.translated, true);
      assert.equal(display.narrative, 'האטם דולף. האטם הוחלף.');
      assert.equal(display.componentLabel, store.component ? translations['maintenanceComponent.driveShaft'].he : null);
      assert.equal(text.includes(hebrewSource), false);
      assert.equal(text.split(display.narrative).length - 1, 1);
      if (display.componentLabel) assert.equal(display.narrative.includes(display.componentLabel), false);
      assert.equal(text.includes('maintenanceComponent.'), false);
      assert.equal(text.includes('maintenanceLog.'), false);
    });

    it(`falls back to the original Hebrew description when the translation is unavailable (${store.name})`, () => {
      const unavailable = response([{ language: 'he', status: 'unavailable', text: null }], hebrewSource);
      const display = localizeMaintenanceDescription(store.description, 'other', unavailable, 'he', translate('he'));

      assert.equal(display.translated, false);
      assert.equal(display.narrative, hebrewSource);
      assert.equal(shown(display).split(hebrewSource).length - 1, 1);
      assert.equal(display.componentLabel, store.component ? translations['maintenanceComponent.driveShaft'].he : null);
    });
  }

  it('keeps the component label separate from the narrative and never repeats it', () => {
    const display = localizeMaintenanceDescription(
      `maintenanceComponent.driveShaft\n${hebrewSource}`,
      null,
      hebrewOriginalResponse,
      'he',
      translate('he')
    );
    assert.equal(display.componentLabel, 'ציר מניע');
    assert.equal(shown(display).split('ציר מניע').length - 1, 1);
  });

  it('every surface renders the description through the same language-driven helper', () => {
    for (const [name, source] of Object.entries(surfaces)) {
      assert.equal(source.includes('localizeMaintenanceDescription('), true, name);
      assert.equal(source.includes('faultTranslation'), false, name);
      assert.equal(/\bTranslation:/.test(source), false, name);
      // No Hebrew-only path: no hardcoded Hebrew text and no per-language branch.
      assert.equal(/[\u0590-\u05FF]/.test(source), false, name);
      assert.equal(/language\s*===\s*['"]he['"]/.test(source), false, name);
      assert.equal(/isRTL/.test(source), false, name);
    }
  });

  it('every surface isolates narrative direction with dir="auto"', () => {
    assert.equal((surfaces.details.match(/dir="auto"/g) ?? []).length, 2);
    assert.equal(surfaces.list.includes('dir="auto"'), true);
    assert.equal(surfaces.list.includes("dir={display.narrative ? 'auto' : undefined}"), true);
    assert.equal(surfaces.clearance.includes('dir="auto"'), true);
  });

  it('details screen refreshes pending translations with bounded cleanup', () => {
    assert.equal(surfaces.details.includes('hasPendingTranslations('), true);
    assert.equal(surfaces.details.includes('MAX_PENDING_REFRESHES'), true);
    assert.equal(surfaces.details.includes('PENDING_REFRESH_INTERVAL_MS'), true);
    assert.equal(surfaces.details.includes('clearTimeout('), true);
  });

  it('the document direction follows the selected UI language', () => {
    const context = read('../context/LanguageContext.tsx');
    assert.equal(context.includes("document.documentElement.dir = isRTL ? 'rtl' : 'ltr'"), true);
    assert.equal(context.includes("const isRTL = language === 'he'"), true);
  });

  it('source-language report without a source translation job preserves original text for source and translates the other two', () => {
    const hebrewSource = 'האטם דולף. הוחלף האטם.';
    const sourceSkippedResponse = response(
      [
        { language: 'he', status: 'unavailable', text: null },
        { language: 'en', status: 'completed', text: 'The seal leaked. Gasket replaced.' },
        { language: 'th', status: 'completed', text: 'ซีลรั่ว เปลี่ยนปะเก็นแล้ว' },
      ],
      hebrewSource
    );
    const v2 = `maintenanceLog.v2\nmaintenanceComponent.driveShaft\n${hebrewSource}`;

    const heDisplay = localizeMaintenanceDescription(v2, 'other', sourceSkippedResponse, 'he', translate('he'));
    assert.equal(heDisplay.translated, false);
    assert.equal(heDisplay.narrative, hebrewSource);
    assert.equal(heDisplay.componentLabel, translations['maintenanceComponent.driveShaft'].he);

    const enDisplay = localizeMaintenanceDescription(v2, 'other', sourceSkippedResponse, 'en', translate('en'));
    assert.equal(enDisplay.translated, true);
    assert.equal(enDisplay.narrative, 'The seal leaked. Gasket replaced.');

    const thDisplay = localizeMaintenanceDescription(v2, 'other', sourceSkippedResponse, 'th', translate('th'));
    assert.equal(thDisplay.translated, true);
    assert.equal(thDisplay.narrative, 'ซีลรั่ว เปลี่ยนปะเก็นแล้ว');
  });
});
