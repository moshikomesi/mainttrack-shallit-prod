export interface FieldCheck {
  fieldId: string;
  value: string;
  errorKey: string;
}

export interface ValidateRequiredResult {
  valid: boolean;
  firstInvalidField: string | null;
  errorKey: string | null;
}

/**
 * Validates that required string fields are non-empty (after trim).
 * Returns the first invalid field id and its translation key for error message.
 */
export function validateRequired(checks: FieldCheck[]): ValidateRequiredResult {
  for (const { fieldId, value, errorKey } of checks) {
    if (value == null || String(value).trim() === '') {
      return {
        valid: false,
        firstInvalidField: fieldId,
        errorKey,
      };
    }
  }
  return {
    valid: true,
    firstInvalidField: null,
    errorKey: null,
  };
}
