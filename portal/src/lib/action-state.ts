/** Result of a server action, rendered by <ActionForm>. Shared by server actions and client components. */
export interface ActionState {
  ok?: boolean;
  message?: string;
  error?: string;
  /**
   * A credential shown exactly once (client secret, API key, webhook signing secret). It exists only in this
   * response: the portal never stores it and the Management API never returns it again.
   */
  secret?: { label: string; value: string; note?: string };
}

export const initialState: ActionState = {};
