import { Button } from "@st/ui/components/ui/button";
import Link from "next/link";

export default function NotFound() {
  return (
    <div className="flex flex-1 flex-col items-center justify-center gap-2 p-4 text-center">
      <h1 className="text-2xl font-semibold">Page not found</h1>
      <p className="text-muted-foreground">
        The page you&apos;re looking for doesn&apos;t exist.
      </p>
      <Button asChild className="mt-4">
        <Link href="/">Go home</Link>
      </Button>
    </div>
  );
}
