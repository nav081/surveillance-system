import { useState } from 'react';
import type { FormEvent } from 'react';
import { Link as RouterLink, useNavigate } from 'react-router-dom';
import { Box, Button, Card, CardContent, Link, Stack, TextField, Typography } from '@mui/material';

import { useAuth } from '../auth/context';
import { ErrorBanner, errorMessage } from '../components/common';

export function RegisterPage() {
  const { register } = useAuth();
  const navigate = useNavigate();
  const [tenantName, setTenantName] = useState('');
  const [fullName, setFullName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await register(tenantName, fullName, email, password);
      navigate('/', { replace: true });
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Box sx={{ minHeight: '100vh', display: 'flex', alignItems: 'center', justifyContent: 'center', p: 2 }}>
      <Card sx={{ width: '100%', maxWidth: 420 }}>
        <CardContent>
          <Typography variant="h5" fontWeight={700} gutterBottom>Create your organisation</Typography>
          <ErrorBanner error={error} />
          <form onSubmit={onSubmit}>
            <Stack spacing={2}>
              <TextField label="Organisation name" required value={tenantName} onChange={(e) => setTenantName(e.target.value)} />
              <TextField label="Your full name" required value={fullName} onChange={(e) => setFullName(e.target.value)} />
              <TextField label="Email" type="email" required autoComplete="username" value={email} onChange={(e) => setEmail(e.target.value)} />
              <TextField
                label="Password"
                type="password"
                required
                autoComplete="new-password"
                helperText="At least 10 characters."
                value={password}
                onChange={(e) => setPassword(e.target.value)}
              />
              <Button type="submit" variant="contained" disabled={busy}>Create account</Button>
              <Typography variant="body2">
                Already registered? <Link component={RouterLink} to="/login">Sign in</Link>
              </Typography>
            </Stack>
          </form>
        </CardContent>
      </Card>
    </Box>
  );
}
