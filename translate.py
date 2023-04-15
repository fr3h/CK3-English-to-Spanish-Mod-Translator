import sys
import os
import requests
from argostranslate import package, translate

translate_package = "translate_packages"

def download_language_package(url, install_path):
    response = requests.get(url)
    response.raise_for_status()
    with open(os.path.join(install_path, os.path.basename(url)), 'wb') as f:
        f.write(response.content)

def check_and_download_language_package(from_code, to_code, install_path):
    package.update_package_index()
    installed_packages = package.get_installed_packages()
    installed_languages = next(
        filter(
            lambda x: x.from_code == from_code and x.to_code == to_code, installed_packages
        ),
        None
    )

    if not installed_languages:
        print(f"{from_code}-{to_code} package not installed. Downloading...")
        available_packages = package.get_available_packages()
        target_package = next(
            filter(
                lambda x: x.from_code == from_code and x.to_code == to_code, available_packages
            ),
            None
        )
        if target_package:
            package.install_from_available(target_package)
            print(f"{from_code}-{to_code} package installed successfully.")
        else:
            print(f"No se encontró el paquete {from_code}-{to_code}.")

def main():
    text = sys.argv[1]
    from_lang = sys.argv[2]
    to_lang = sys.argv[3]

    check_and_download_language_package(from_lang, to_lang, translate_package)

    translator = translate.Translator(from_lang=from_lang, to_lang=to_lang)
    translated_text = translator.translate(text)
    print(translated_text)

if __name__ == "__main__":
    main()

    