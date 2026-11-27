export function explorePage(imageCount: number, columns: number, anchor: number) {
  const pageSize = columns * 3;
  const pageCount = Math.ceil(imageCount / pageSize);
  const page = Math.min(Math.floor(anchor / pageSize) + 1, Math.max(1, pageCount));
  const start = (page - 1) * pageSize;
  const firstNumber = Math.min(Math.max(1, page - 4), Math.max(1, pageCount - 9));
  const numbers = Array.from(
    { length: Math.min(10, pageCount) },
    (_, i) => firstNumber + i,
  );
  return { page, pageCount, pageSize, start, numbers };
}

export const exploreBatchSize = 100;

export async function collectExplorePages<T extends { id: string }>(
  fetchPage: (offset: number) => Promise<T[]>,
) {
  const items = new Map<string, T>();
  for (let offset = 0; ; offset += exploreBatchSize) {
    const batch = await fetchPage(offset);
    const previousSize = items.size;
    batch.forEach(item => items.set(item.id, item));
    // A server without offset support can return the same first batch again.
    if (batch.length < exploreBatchSize || items.size === previousSize) return [...items.values()];
  }
}
