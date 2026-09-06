"use client";

import { Button } from "@st/ui/components/ui/button";
import { useEffect } from "react";

export default function ErrorPage({
  error,
  retry,
}: {
  error: Error & { digest?: string };
  retry: () => void;
}) {
  useEffect(() => {
    console.error(error);
  }, [error]);

  return (
    <div className="flex flex-1 flex-col items-center justify-center gap-2 p-4 text-center">
      <h1 className="text-2xl font-semibold">Something went wrong</h1>
      <p className="text-muted-foreground">
        An unexpected error occurred. You can try again.
      </p>
      <Button onClick={() => retry()} className="mt-4">
        Try again
      </Button>
    </div>
  );
}
