import { useEffect, useState } from 'react';
import { Box, Typography, Alert, Stack } from '@mui/material';

export function errorMessage(err: unknown): string {
  return err instanceof Error ? err.message : 'Something went wrong.';
}

export function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return '—';
  return new Date(iso).toLocaleString();
}

export function formatDuration(seconds: number | null | undefined): string {
  if (seconds === null || seconds === undefined) return '—';
  const h = Math.floor(seconds / 3600);
  const m = Math.floor((seconds % 3600) / 60);
  const s = seconds % 60;
  if (h > 0) return `${h}h ${m}m`;
  if (m > 0) return `${m}m ${s}s`;
  return `${s}s`;
}

/** Triggers a browser download for a Blob. */
export function downloadBlob(blob: Blob, filename: string) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(url);
}

/** Shows an error banner when there is an error. */
export function ErrorBanner({ error }: { error: string | null }) {
  if (!error) return null;
  return <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>;
}

export function PageHeader({ title, actions }: { title: string; actions?: React.ReactNode }) {
  return (
    <Stack direction="row" alignItems="center" justifyContent="space-between" sx={{ mb: 3 }} spacing={2}>
      <Typography variant="h5" fontWeight={700}>
        {title}
      </Typography>
      <Box>{actions}</Box>
    </Stack>
  );
}

/** Loads an authenticated image (snapshots need the bearer token, so <img src> alone cannot be used). */
export function useObjectUrl(loader: (() => Promise<Blob>) | null): { url: string | null; error: string | null } {
  const [url, setUrl] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    if (!loader) return;
    let created: string | null = null;
    let cancelled = false;
    loader()
      .then((blob) => {
        if (cancelled) return;
        created = URL.createObjectURL(blob);
        setUrl(created);
      })
      .catch((e) => { if (!cancelled) setError(errorMessage(e)); });
    return () => {
      cancelled = true;
      if (created) URL.revokeObjectURL(created);
    };
  }, [loader]);
  return { url, error };
}
