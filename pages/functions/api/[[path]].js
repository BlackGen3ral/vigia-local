export function onRequest({ request, env }) {
  return env.BACKEND.fetch(request);
}
