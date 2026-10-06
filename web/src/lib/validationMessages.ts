/** Canonical validation message for a value longer than `max` characters (ADR-0006). */
export function maxLengthMessage(max: number): string {
  return `Must be ${max} characters or fewer.`
}
