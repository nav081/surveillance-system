import { useCallback, useEffect, useState } from 'react';
import { Card, CardContent, Grid, Typography, CircularProgress, Box, Button } from '@mui/material';
import RefreshIcon from '@mui/icons-material/Refresh';

import { api } from '../api/client';
import type { DashboardDto } from '../api/types';
import { ErrorBanner, PageHeader, errorMessage } from '../components/common';

interface Tile {
  label: string;
  value: number;
  color: string;
}

export function DashboardPage() {
  const [data, setData] = useState<DashboardDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const load = useCallback(() => {
    setLoading(true);
    api.dashboard()
      .then((d) => { setData(d); setError(null); })
      .catch((e) => setError(errorMessage(e)))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    load();
    const id = window.setInterval(load, 30_000);
    return () => window.clearInterval(id);
  }, [load]);

  const tiles: Tile[] = data
    ? [
        { label: 'Total homes', value: data.totalHomes, color: '#1565c0' },
        { label: 'Total cameras', value: data.totalCameras, color: '#00897b' },
        { label: 'Cameras online', value: data.onlineCameras, color: '#2e7d32' },
        { label: 'People currently inside', value: data.peopleCurrentlyInside, color: '#6a1b9a' },
        { label: 'Entries today', value: data.entriesToday, color: '#ef6c00' },
        { label: 'Exits today', value: data.exitsToday, color: '#c62828' },
      ]
    : [];

  return (
    <>
      <PageHeader
        title="Dashboard"
        actions={<Button startIcon={<RefreshIcon />} onClick={load} disabled={loading}>Refresh</Button>}
      />
      <ErrorBanner error={error} />
      {!data && loading && <Box sx={{ textAlign: 'center', mt: 6 }}><CircularProgress /></Box>}
      <Grid container spacing={2}>
        {tiles.map((t) => (
          <Grid item xs={12} sm={6} md={4} key={t.label}>
            <Card sx={{ borderLeft: `6px solid ${t.color}` }}>
              <CardContent>
                <Typography variant="body2" color="text.secondary">{t.label}</Typography>
                <Typography variant="h4" fontWeight={700}>{t.value}</Typography>
              </CardContent>
            </Card>
          </Grid>
        ))}
      </Grid>
      {data && (
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 2 }}>
          Updated {new Date(data.generatedAt).toLocaleTimeString()} (auto-refreshes every 30 seconds)
        </Typography>
      )}
    </>
  );
}
