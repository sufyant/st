import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { SignInForm } from "@/lib/auth/components";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("auth");
  return { title: t("signIn") };
}

export default function SignInPage() {
  return (
    <div className="flex min-h-screen items-center justify-center">
      <SignInForm />
    </div>
  );
}
