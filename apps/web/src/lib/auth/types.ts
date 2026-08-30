/**
 * Our session shape. Deliberately not Clerk's.
 *
 * This type is the contract the rest of the app codes against, which is what
 * makes the identity provider replaceable: swapping vendors changes the mapping
 * in this folder and nothing else.
 *
 * When packages/shared exists, this moves there so apps/admin and apps/mobile
 * describe a session the same way.
 */
export type Session = {
  /**
   * The identity provider's id for this person — Clerk's `user_...` today.
   *
   * Maps to `users.external_auth_id` in our database and nowhere else. It is
   * not our user id: never store it on another table, put it in a URL, use it
   * as a cache key, or treat it as stable across a provider change.
   */
  externalAuthId: string;

  /** Primary email as the provider knows it. Display only — our own user
   *  record is the authority on anything we act on. */
  email: string | null;
};

/**
 * What the provider is NOT asked for: tenant membership, roles, permissions.
 * Those live in our database, keyed by externalAuthId, and are read through the
 * API. A claim that appears in the provider's token is not authorization.
 */
