export function formatTechnician(
  translate: (key: string) => string,
  technicianCode: string
): string {
  const code = technicianCode.trim();
  if (!code) return '';

  const key = `tech.${code}`;
  const label = translate(key);
  return label === key ? code : label;
}
